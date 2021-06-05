using System;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ClientMJPEG
{
    public class ImageCreator : IDisposable
    {
        private static byte[] _contentLengthBytes = Encoding.UTF8.GetBytes("\nContent-Length: ");
        private static byte[] _newLineBytes = Encoding.UTF8.GetBytes("\n");
        private static int _carriageReturnSize = Encoding.UTF8.GetBytes("\r").Length;

        private Task _routine;
        private volatile bool _stop = false;
        private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

        private EndPoint _endPoint;
        private HttpClient _client;

        /// <param name="endpoint">endpoint http stream</param>
        public ImageCreator(EndPoint endpoint)
        {
            _endPoint = endpoint;
        }

        public ChannelReader<byte[]> ImageByteReader => _channel.Reader;

        public bool Start()
        {
            if (_client == null)
                _client = new HttpClient(_endPoint);

            var result = _client.ConnectAsync();
            if (!result)
                return false;

            _routine = Task.Factory.StartNew<Task>(async () =>
            {
                try
                {
                    using (var stream = await _client.GetStream())
                    {
                        _client.RequestGetOnStream("/mjpg/video.mjpg");

                        var packageSize = 50000;
                        var currentPackageSize = packageSize;

                        var partImageBuffer = new Memory<byte>(new byte[2 * packageSize]);
                        var partImageBufferSize = 0;

                        var readBuffer = new Memory<byte>(new byte[packageSize]);

                        var readTask = stream.ReadAsync(readBuffer);
                        while (stream.CanRead && !_stop)
                        {
                            var size = await readTask;

                            if (packageSize > currentPackageSize)
                            {
                                var newBuffer = new Memory<byte>(new byte[2 * packageSize]);
                                
                                partImageBuffer.CopyTo(newBuffer);
                                partImageBuffer = newBuffer;
                            }

                            FillPartImageBuffer(readBuffer, partImageBuffer, partImageBufferSize);
                            partImageBufferSize = partImageBufferSize + size;

                            if (packageSize > currentPackageSize)
                                readBuffer = new Memory<byte>(new byte[packageSize]);

                            readTask = stream.ReadAsync(readBuffer);

                            currentPackageSize = packageSize;
                            var processOffset = 0;
                            var process = true;
                            while (process)
                            {
                                var prcessSlice = partImageBuffer.Slice(processOffset, partImageBufferSize - processOffset);
                                var indexContentLengthStart = prcessSlice.FindBytesIndex(prcessSlice.Length, _contentLengthBytes);
                                if (indexContentLengthStart == -1)
                                {
                                    //write part and process next package from stream
                                    process = false;
                                    partImageBufferSize = FillPartImageBuffer(prcessSlice, partImageBuffer, 0);
                                    continue;
                                }

                                var indexContentLengthEnd = indexContentLengthStart + _contentLengthBytes.Length;
                                if (indexContentLengthEnd > prcessSlice.Length)
                                {
                                    //write part and process next package from stream
                                    process = false;
                                    partImageBufferSize = FillPartImageBuffer(prcessSlice, partImageBuffer, 0);
                                    continue;
                                }

                                var indexEndLength = prcessSlice.Slice(indexContentLengthEnd).FindBytesIndex(prcessSlice.Length, _newLineBytes);
                                if (indexEndLength == -1)
                                {
                                    //write part and process next package from stream
                                    process = false;
                                    partImageBufferSize = FillPartImageBuffer(prcessSlice, partImageBuffer, 0);
                                    continue;
                                }

                                indexEndLength += indexContentLengthEnd;
                                if (indexEndLength - (indexContentLengthEnd + 1) > prcessSlice.Length)
                                {
                                    //write part and process next package from stream
                                    process = false;
                                    partImageBufferSize = FillPartImageBuffer(prcessSlice, partImageBuffer, 0);
                                    continue;
                                }

                                var lengthStr = Encoding.UTF8.GetString(prcessSlice.Span.Slice(indexContentLengthEnd, indexEndLength - (indexContentLengthEnd + 1)));
                                var imageSize = int.Parse(lengthStr);
                                if (imageSize > currentPackageSize)
                                    packageSize = imageSize;

                                var imageStartIndex = indexEndLength + (_newLineBytes.Length * 2 + _carriageReturnSize);

                                if (imageStartIndex + imageSize > prcessSlice.Length)
                                {
                                    //write part and process next package from stream
                                    process = false;
                                    partImageBufferSize = FillPartImageBuffer(prcessSlice, partImageBuffer, 0);
                                    continue;
                                }

                                if (imageStartIndex + imageSize == prcessSlice.Length)
                                {
                                    partImageBufferSize = 0;
                                    process = false;
                                }
                                else
                                {
                                    _channel.Writer.TryWrite(prcessSlice.Span.Slice(imageStartIndex, imageSize).ToArray());
                                    processOffset += imageStartIndex + imageSize;
                                }
                            }
                        }
                    }
                }
                catch { }
            }, TaskCreationOptions.LongRunning);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FillPartImageBuffer(Memory<byte> memorySource, Memory<byte> memoryDestination, int destinationStartIndex)
        {
            for (int i = 0; i < memorySource.Span.Length; i++)
                memoryDestination.Span[destinationStartIndex + i] = memorySource.Span[i];

            return memorySource.Length;
        }

        public bool Stop()
        {
            _stop = true;
            return _client?.DisconnectAsync() ?? false;
        }

        private bool _isDisposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposingManagedResources)
        {
            if (!_isDisposed)
            {
                if (disposingManagedResources)
                {
                    _client?.Dispose();
                    try
                    {
                        _routine?.Wait();
                        _routine?.Dispose();
                    }
                    catch { /*ignore*/ }
                }

                _isDisposed = true;
            }
        }

        ~ImageCreator()
        {
            Dispose(false);
        }
    }
}

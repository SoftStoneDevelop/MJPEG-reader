using System;
using System.Buffers;
using System.Net;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ClientMJPEG
{
    public class ImageCreator : IDisposable
    {
        private static readonly byte[] _boundaryMark = Encoding.UTF8.GetBytes("boundary=");


        private Task _routine;
        private volatile bool _stop = false;
        private readonly Channel<(IMemoryOwner<byte>, int)> _channel = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true }
            );

        private EndPoint _endPoint;
        private string _path;
        private HttpClient _client;

        /// <param name="endpoint">endpoint http stream</param>
        public ImageCreator(EndPoint endpoint, string path)
        {
            _path = path;
            _endPoint = endpoint;
        }

        public ChannelReader<(IMemoryOwner<byte>, int)> ImageByteReader => _channel.Reader;

        public bool Start()
        {
            if (_client == null)
                _client = new HttpClient(_endPoint);

            var result = _client.ConnectAsync();
            if (!result)
                return false;

            _routine = Task.Factory.StartNew(async () =>
            {
                try
                {
                    using (var stream = await _client.GetStream())
                    {
                        _client.RequestGetOnStream(_path);

                        var indxBoundary = 0;
                        while (!_stop && stream.CanRead)
                        {
                            var readByte = stream.ReadByte();
                            if (readByte == -1)
                            {
                                return;
                            }

                            var foundMark = false;

                            if ((byte)readByte == _boundaryMark[indxBoundary])
                            {
                                if (indxBoundary == _boundaryMark.Length - 1)
                                {
                                    foundMark = true;
                                }

                                indxBoundary++;
                            }
                            else
                            {
                                indxBoundary = 0;
                            }

                            if (foundMark)
                            {
                                break;
                            }
                        }

                        var boundary = new byte[70];
                        var boundarySize = 0;
                        while (!_stop)
                        {
                            var readByte = stream.ReadByte();
                            if (readByte == -1)
                            {
                                return;
                            }

                            if(readByte == '"')
                            {
                                continue;
                            }

                            if (readByte == '\r')
                            {
                                break;
                            }

                            boundary[boundarySize++] = (byte)readByte;
                        }

                        var readBufferSize = 4000;
                        var readBuffer = MemoryPool<byte>.Shared.Rent(readBufferSize);
                        readBufferSize = readBuffer.Memory.Length;

                        var imageBufferSize = readBufferSize;
                        var imageBuffer = MemoryPool<byte>.Shared.Rent(imageBufferSize);
                        imageBufferSize = imageBuffer.Memory.Length;
                        var payloadSize = 0;
                        var payloadOffset = 0;

                        var startDataMark = new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };

                        try
                        {
                            var readTask = stream.ReadAsync(readBuffer.Memory.Slice(0, readBufferSize));
                            while (!_stop && stream.CanRead)
                            {
                                var readSize = await readTask;
                                if(imageBufferSize - payloadSize < readBufferSize)
                                {
                                    var newImageBuffer = MemoryPool<byte>.Shared.Rent(imageBufferSize * 2);
                                    imageBuffer.Memory.Slice(payloadOffset, payloadSize).CopyTo(newImageBuffer.Memory);
                                    payloadOffset = 0;
                                    imageBuffer.Dispose();
                                    imageBuffer = newImageBuffer;
                                    imageBufferSize = imageBuffer.Memory.Length;
                                }

                                if(imageBufferSize - payloadOffset - payloadSize < readBufferSize)
                                {
                                    var payloadData = imageBuffer.Memory.Slice(payloadOffset, payloadSize);
                                    var all = imageBuffer.Memory.Slice(0);

                                    int dest = 0;
                                    for (int i = payloadOffset; i < payloadData.Length; i++)
                                    {
                                        all.Span[dest++] = payloadData.Span[i];
                                    }

                                    imageBuffer.Memory.Slice(payloadOffset, payloadSize).CopyTo(imageBuffer.Memory);
                                    payloadOffset = 0;
                                }

                                readBuffer.Memory.Slice(0, readSize).CopyTo(imageBuffer.Memory.Slice(payloadOffset + payloadSize));
                                payloadSize += readSize;
                                readTask = stream.ReadAsync(readBuffer.Memory.Slice(0, readBufferSize));

                                var prcessSlice = imageBuffer.Memory.Slice(payloadOffset, payloadSize);
                                var boundaryIndex = prcessSlice.FindBytesIndex(boundary, boundarySize);
                                if (boundaryIndex == -1)
                                {
                                    continue;
                                }

                                var startData = prcessSlice.Slice(boundaryIndex + boundarySize).FindBytesIndex(startDataMark, startDataMark.Length);
                                if (startData == -1)
                                {
                                    continue;
                                }

                                startData += boundaryIndex + boundarySize + startDataMark.Length;
                                if (startData > prcessSlice.Length)
                                {
                                    continue;
                                }

                                //next boundary is the end of prev
                                var nextBoundaryIndex = prcessSlice.Slice(startData).FindBytesIndex(boundary, boundarySize);
                                if (nextBoundaryIndex == -1)
                                {
                                    continue;
                                }

                                nextBoundaryIndex -= 2;//-- marker
                                if(nextBoundaryIndex < 0)
                                {
                                    continue;
                                }

                                prcessSlice = prcessSlice.Slice(startData, nextBoundaryIndex);

                                var memory = MemoryPool<byte>.Shared.Rent(nextBoundaryIndex);
                                try
                                {
                                    prcessSlice.Span.CopyTo(memory.Memory.Span);
                                    _channel.Writer.TryWrite((memory, nextBoundaryIndex));
                                }
                                catch
                                {
                                    memory.Dispose();
                                    throw;
                                }

                                payloadSize -= startData + nextBoundaryIndex;
                                payloadOffset += startData + nextBoundaryIndex;
                            }
                        }
                        finally
                        {
                            imageBuffer.Dispose();
                            readBuffer.Dispose();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //ignore
                }
            }, TaskCreationOptions.LongRunning);

            return true;
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

                    _channel.Writer.Complete();
                    while (_channel.Reader.TryRead(out var data))
                    {
                        data.Item1.Dispose();
                    }
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

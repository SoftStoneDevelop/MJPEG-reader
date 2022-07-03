using System;
using System.Buffers;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ClientMJPEG
{
    public class ImageCreator : IDisposable
    {
        private static readonly byte[] _contentLengthBytes = Encoding.UTF8.GetBytes("\nContent-Length: ");
        private static readonly byte[] _newLineBytes = Encoding.UTF8.GetBytes("\n");
        private static readonly int _carriageReturnSize = Encoding.UTF8.GetBytes("\r").Length;

        private Task _routine;
        private volatile bool _stop = false;
        private readonly Channel<IMemoryOwner<byte>> _channel = Channel.CreateUnbounded<IMemoryOwner<byte>>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true }
            );

        private EndPoint _endPoint;
        private HttpClient _client;

        /// <param name="endpoint">endpoint http stream</param>
        public ImageCreator(EndPoint endpoint)
        {
            _endPoint = endpoint;
        }

        public ChannelReader<IMemoryOwner<byte>> ImageByteReader => _channel.Reader;

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
                        _client.RequestGetOnStream("/mjpg/video.mjpg");

                        var packageSize = 500;
                        var currentPackageSize = packageSize;

                        var imageBuffer = MemoryPool<byte>.Shared.Rent(2 * packageSize);
                        var payloadSize = 0;

                        var readBuffer = MemoryPool<byte>.Shared.Rent(packageSize * 2);

                        var lengthImageBuffer = MemoryPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(sizeof(int)));

                        try
                        {
                            var readTask = stream.ReadAsync(readBuffer.Memory.Slice(0, packageSize * 2));
                            while (!_stop && stream.CanRead)
                            {
                                var readSize = await readTask;
                                if(packageSize > imageBuffer.Memory.Length)
                                {
                                    var newImageBuffer = MemoryPool<byte>.Shared.Rent(packageSize * 2);
                                    imageBuffer.Memory.CopyTo(newImageBuffer.Memory);
                                    imageBuffer.Dispose();
                                    imageBuffer = newImageBuffer;
                                }
                                else
                                if (readSize > imageBuffer.Memory.Length - payloadSize)//this happens if the size of the image is not yet known
                                {
                                    var newImageBuffer = MemoryPool<byte>.Shared.Rent((payloadSize + readSize) * 2);
                                    imageBuffer.Memory.CopyTo(newImageBuffer.Memory);
                                    imageBuffer.Dispose();
                                    imageBuffer = newImageBuffer;
                                }

                                if (packageSize > readBuffer.Memory.Length)
                                {
                                    var newReadBuffer = MemoryPool<byte>.Shared.Rent(packageSize * 2);
                                    readBuffer.Memory.CopyTo(newReadBuffer.Memory);
                                    readBuffer.Dispose();
                                    readBuffer = newReadBuffer;
                                }

                                readBuffer.Memory.Slice(0, readSize).CopyTo(imageBuffer.Memory.Slice(payloadSize));
                                payloadSize += readSize;

                                readTask = stream.ReadAsync(readBuffer.Memory.Slice(0, packageSize * 2));

                                var processOffset = 0;
                                var process = true;
                                while (process)
                                {
                                    var prcessSlice = imageBuffer.Memory.Slice(processOffset, payloadSize - processOffset);
                                    var indexContentLengthStart = prcessSlice.FindBytesIndex(_contentLengthBytes);
                                    if (indexContentLengthStart == -1)
                                    {
                                        process = false;
                                        copyPayloadDataToStart();
                                        continue;
                                    }

                                    var indexContentLengthEnd = indexContentLengthStart + _contentLengthBytes.Length;
                                    if (indexContentLengthEnd > prcessSlice.Length)
                                    {
                                        process = false;
                                        copyPayloadDataToStart();
                                        continue;
                                    }

                                    prcessSlice = prcessSlice.Slice(indexContentLengthEnd);
                                    var indexEndLength = prcessSlice.FindBytesIndex(_newLineBytes);
                                    if (indexEndLength == -1)
                                    {
                                        process = false;
                                        copyPayloadDataToStart();
                                        continue;
                                    }

                                    var charsCount = Encoding.UTF8.GetCharCount(prcessSlice.Span.Slice(0, indexEndLength));
                                    Encoding.UTF8.GetChars(
                                        prcessSlice.Span.Slice(0, indexEndLength),
                                        lengthImageBuffer.Memory.Span
                                        );
                                    prcessSlice = prcessSlice.Slice(indexEndLength);
                                    var imageSize = int.Parse(lengthImageBuffer.Memory.Span.Slice(0, charsCount));
                                    if (imageSize > currentPackageSize)
                                        packageSize = imageSize;

                                    if(prcessSlice.Length <= _newLineBytes.Length * 2 + _carriageReturnSize)
                                    {
                                        process = false;
                                        copyPayloadDataToStart();
                                        continue;
                                    }
                                    else
                                    {
                                        prcessSlice = prcessSlice.Slice(_newLineBytes.Length * 2 + _carriageReturnSize);
                                    }

                                    if(prcessSlice.Length < imageSize)
                                    {
                                        process = false;
                                        copyPayloadDataToStart();
                                        continue;
                                    }

                                    if(imageSize == prcessSlice.Length)
                                    {
                                        payloadSize = 0;
                                        process = false;
                                    }
                                    else
                                    {
                                        processOffset += prcessSlice.Length;
                                    }

                                    prcessSlice = prcessSlice.Slice(0, imageSize);
                                    var memory = MemoryPool<byte>.Shared.Rent(imageSize);
                                    try
                                    {
                                        prcessSlice.Span.CopyTo(memory.Memory.Span);
                                        _channel.Writer.TryWrite(memory);
                                    }
                                    catch
                                    {
                                        memory.Dispose();
                                        throw;
                                    }

                                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                    void copyPayloadDataToStart()
                                    {
                                        prcessSlice = imageBuffer.Memory.Slice(processOffset, payloadSize - processOffset);
                                        payloadSize -= processOffset;
                                        prcessSlice.CopyTo(imageBuffer.Memory);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            imageBuffer.Dispose();
                            readBuffer.Dispose();
                            lengthImageBuffer.Dispose();
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
                    while (_channel.Reader.TryRead(out var memoryOwner))
                    {
                        memoryOwner.Dispose();
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

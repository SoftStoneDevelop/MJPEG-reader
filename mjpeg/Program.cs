using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace mjpeg
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var client = new ImageCreator(new IPEndPoint(IPAddress.Parse("187.150.70.217"), 8083));
            client.Start();

            var counter = 0;
            var task = Task.Factory.StartNew(async () =>
            {
                while (await client.ImageByteReader.WaitToReadAsync())
                {
                    var imageData = await client.ImageByteReader.ReadAsync();

                    using (var ms = new MemoryStream(imageData))
                    {
                        var image = System.Drawing.Image.FromStream(ms);
                        image.Save(@$"E:\work\mjpeg task\mjpeg\image{++counter}.jpg");
                    }
                }
            });

            Console.ReadLine();
            task.Wait();
        }

        public class ImageCreator : IDisposable
        {
            private static byte[] _contentLengthBytes = Encoding.UTF8.GetBytes("\nContent-Length: ");
            private static byte[] _newLineBytes = Encoding.UTF8.GetBytes("\n");
            private static int _carriageReturnSize = Encoding.UTF8.GetBytes("\r").Length;

            private Task _routine;
            private volatile bool _stop = false;
            private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions(){SingleWriter = true, SingleReader = true});

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

                            var partImageBuffer = new Memory<byte>(new byte[2 * 50000]);
                            var flushBuffer = new Memory<byte>(new byte[50000]);
                            var partImageBufferSize = 0;

                            var readBuffer = new Memory<byte>(new byte[50000]);

                            var readTask = stream.ReadAsync(readBuffer);
                            var switchBuffer = 0;
                            while (stream.CanRead && !_stop)
                            {
                                var size = await readTask;
                                Memory<byte> memory;
                                if (switchBuffer == 0)
                                {
                                    readTask = stream.ReadAsync(flushBuffer);
                                    switchBuffer = 1;
                                    FillPartImageBuffer(readBuffer, partImageBuffer, partImageBufferSize);
                                    partImageBufferSize = partImageBufferSize + size;
                                    memory = partImageBuffer;
                                }
                                else
                                {
                                    readTask = stream.ReadAsync(readBuffer);
                                    switchBuffer = 0;
                                    FillPartImageBuffer(flushBuffer, partImageBuffer, partImageBufferSize);
                                    partImageBufferSize = partImageBufferSize + size;
                                    memory = partImageBuffer;
                                }

                                var processOffset = 0;
                                var process = true;
                                while (process)
                                {
                                    var prcessSlice = memory.Slice(processOffset, partImageBufferSize - processOffset);
                                    var indexContentLengthStart = FindBytesIndex(prcessSlice, prcessSlice.Length, _contentLengthBytes);
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

                                    var indexEndLength = FindBytesIndex(prcessSlice.Slice(indexContentLengthEnd), prcessSlice.Length, _newLineBytes);
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

        /// <summary>
        /// Find first equal bytes in source
        /// </summary>
        /// <returns>-1 if not find in source</returns>
        public static int FindBytesIndex(
            Memory<byte> source,
            int size,
            Memory<byte> pattern
            )
        {
            var index = -1;
            for (int i = 0; i < size; i++)
            {
                if (size - i < pattern.Length)
                    return index;

                var temp = source.Slice(i, pattern.Length);
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (temp.Span[j] == pattern.Span[j])
                    {
                        if (j == pattern.Length -1)
                        {
                            index = i;
                            return index;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return index;
        }
    }

    public class HttpClient : IDisposable
    {
        private object _streamLock = new();
        private NetworkStream _stream;

        private TaskCompletionSource<NetworkStream> _tcs;
        private SocketAsyncEventArgs _connectEventArg;

        public HttpClient(EndPoint endpoint)
        {
            EndPoint = endpoint;
        }

        public EndPoint EndPoint { get; init; }

        public Socket Socket { get; private set; }

        public volatile bool IsConnected;

        public volatile bool IsConnecting;

        public volatile bool IsSocketDisposed = true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Socket CreateSocket()
        {
            return new Socket(EndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        }

        public bool ConnectAsync()
        {
            if (IsConnected || IsConnecting)
                return false;

            IsConnecting = true;

            Socket = CreateSocket();

            _connectEventArg = new SocketAsyncEventArgs();
            _connectEventArg.RemoteEndPoint = EndPoint;
            _connectEventArg.Completed += OnAsyncCompleted;

            return Socket.ConnectAsync(_connectEventArg);
        }

        public bool DisconnectAsync()
        {
            if (!IsConnected && !IsConnecting)
                return false;

            if (IsConnecting)
                Socket.CancelConnectAsync(_connectEventArg);

            try
            {
                try
                {
                    Socket.Shutdown(SocketShutdown.Both);
                }
                catch (SocketException) { }

                Socket?.Close();
                Socket?.Dispose();

                // Dispose event arguments
                _connectEventArg?.Dispose();
                _tcs?.Task.Dispose();
                _stream?.Dispose();

                IsSocketDisposed = true;
            }
            catch (ObjectDisposedException) { }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.LastOperation == SocketAsyncOperation.Connect && e.SocketError == SocketError.Success)
            {
                IsConnecting = false;
                IsConnected = true;

                lock (_streamLock)
                {
                    if (_stream == null && _tcs != null && !_tcs.Task.IsCompleted)
                        _tcs.SetResult(CreateStream());
                }
            }
        }

        public Task<NetworkStream> GetStream()
        {
            lock (_streamLock)
            {
                _tcs = new TaskCompletionSource<NetworkStream>();

                if (IsConnected)
                    _tcs.SetResult(CreateStream());

                return _tcs.Task;
            }
        }

        private NetworkStream CreateStream()
        {
            _stream = new NetworkStream(Socket, true);
            return _stream;
        }

        public bool RequestGetOnStream(string url)
        {
            lock (_streamLock)
            {
                if (!IsConnected || _stream == null)
                    return false;
            }

            var request = $"GET {url} HTTP/1.1\r\nHost: {((IPEndPoint)EndPoint).Address}\r\nContent-Length: 0\r\n\r\n";

            var data = new byte[Encoding.UTF8.GetMaxByteCount(request.Length)];
            Encoding.UTF8.GetBytes(request, 0, request.Length, data, 0);

            _stream.Write(data);

            return true;
        }

        #region IDisposable implementation

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposingManagedResources)
        {
            if (!IsDisposed)
            {
                if (disposingManagedResources)
                    DisconnectAsync();

                _streamLock = null;

                IsDisposed = true;
            }
        }

        ~HttpClient()
        {
            Dispose(false);
        }

        #endregion
    }
}
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace mjpeg
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var client = new HttpClient(new IPEndPoint(IPAddress.Parse("187.150.70.217"), 8083));
            client.ConnectAsync();

            using (var s = await client.GetStream())
            {
                client.RequestGetOnStream(@"187.150.70.217", "/mjpg/video.mjpg");

                while (s.CanRead)
                {
                    var readCache = new Memory<byte>(new byte[50000]);
                    var size = await s.ReadAsync(readCache);

                    var contentLengthBytes = Encoding.UTF8.GetBytes("\nContent-Length: ");
                    var emptyBytes = Encoding.UTF8.GetBytes("\n");

                    var indexContentLengthStart = FindBytesIndex(readCache, size, contentLengthBytes);
                    var indexContentLengthEnd = indexContentLengthStart + contentLengthBytes.Length;

                    var indexEndLength = FindBytesIndex(readCache.Slice(indexContentLengthEnd), size, emptyBytes);
                    indexEndLength = indexEndLength + indexContentLengthEnd;

                    var str = Encoding.UTF8.GetString(readCache.Span.Slice(indexContentLengthEnd, indexEndLength - (indexContentLengthEnd + 1)));
                    Console.WriteLine(str);

                    var f = File.CreateText(@"E:\work\mjpeg task\mjpeg\gh.txt");
                    f.Write(Encoding.ASCII.GetString(readCache.Span.Slice(indexContentLengthStart)));

                    return;
                }
            }
            Console.ReadLine();
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

    public class HttpClient
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

        public volatile bool IsReceived;

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

        public virtual void OnReceived(byte[] data) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Connect:
                    {
                        if (e.SocketError == SocketError.Success)
                        {
                            IsConnecting = false;
                            IsConnected = true;

                            lock (_streamLock)
                            {
                                if (_stream == null && _tcs != null && !_tcs.Task.IsCompleted)
                                    _tcs.SetResult(CreateStream());
                            }
                        }
                        break;
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

        public bool RequestGetOnStream(string host, string url)
        {
            lock (_streamLock)
            {
                if (!IsConnected || _stream == null)
                    return false;
            }

            var request = $"GET {url} HTTP/1.1\r\nHost: {host}\r\nContent-Length: 0\r\n\r\n";

            var data = new byte[Encoding.UTF8.GetMaxByteCount(request.Length)];
            Encoding.UTF8.GetBytes(request, 0, request.Length, data, 0);

            _stream.Write(data);

            return true;
        }
    }
}
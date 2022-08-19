using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ClientMJPEG
{
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
                catch (SocketException) { /* ignore*/ }

                Socket?.Close();
                Socket?.Dispose();

                // Dispose event arguments
                _connectEventArg?.Dispose();
                _tcs?.Task.Dispose();
                _stream?.Dispose();

                IsSocketDisposed = true;
            }
            catch (ObjectDisposedException) { /* ignore*/ }

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
            var realSize = Encoding.UTF8.GetBytes(request, 0, request.Length, data, 0);
            _stream.Write(data, 0, realSize);
            Socket.Blocking = false;

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
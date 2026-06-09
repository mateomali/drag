using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FileSender.Network
{
    internal sealed class TransferServer : IDisposable
    {
        private readonly string _sharedKey;
        private readonly int _port;
        private TcpListener _listener;
        private CancellationTokenSource _cts;

        public event Action<PeerConnection> ClientAccepted;
        public event Action<string> StatusChanged;

        public TransferServer(string sharedKey, int port)
        {
            _sharedKey = sharedKey;
            _port = port;
        }

        public void Start()
        {
            if (_listener != null) return;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            RaiseStatus("Servidor escuchando en puerto " + _port);
            Task.Run(() => AcceptLoop(_cts.Token));
        }

        public void Stop()
        {
            if (_cts != null) _cts.Cancel();
            if (_listener != null) _listener.Stop();
            _listener = null;
            RaiseStatus("Servidor detenido");
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    var connection = PeerConnection.FromAcceptedClient(client, _sharedKey);
                    if (ClientAccepted != null) ClientAccepted(connection);
                    connection.Start();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    RaiseStatus("Error aceptando cliente: " + ex.Message);
                }
            }
        }

        private void RaiseStatus(string message)
        {
            if (StatusChanged != null) StatusChanged(message);
        }

        public void Dispose()
        {
            Stop();
            if (_cts != null) _cts.Dispose();
        }
    }
}

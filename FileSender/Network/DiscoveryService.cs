using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileSender.Network
{
    internal sealed class DiscoveredServer
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public int Port { get; set; }

        public override string ToString()
        {
            return Name + " (" + Address + ":" + Port + ")";
        }
    }

    internal sealed class DiscoveryService : IDisposable
    {
        private const string Probe = "FILE_SENDER_DISCOVER";
        private const string Reply = "FILE_SENDER_HERE";

        private readonly string _name;
        private readonly int _tcpPort;
        private UdpClient _listener;
        private CancellationTokenSource _cts;

        public DiscoveryService(string name, int tcpPort)
        {
            _name = name;
            _tcpPort = tcpPort;
        }

        public void StartResponder()
        {
            if (_listener != null) return;
            _cts = new CancellationTokenSource();
            _listener = new UdpClient(FileTransferProtocol.DiscoveryPort);
            Task.Run(() => RespondLoop(_cts.Token));
        }

        public void StopResponder()
        {
            if (_cts != null) _cts.Cancel();
            if (_listener != null) _listener.Close();
            _listener = null;
        }

        public static async Task<List<DiscoveredServer>> DiscoverAsync(int timeoutMilliseconds)
        {
            var results = new Dictionary<string, DiscoveredServer>();
            using (var client = new UdpClient())
            {
                client.EnableBroadcast = true;
                byte[] probe = Encoding.UTF8.GetBytes(Probe);
                foreach (IPAddress address in GetBroadcastAddresses())
                {
                    try
                    {
                        await client.SendAsync(probe, probe.Length, new IPEndPoint(address, FileTransferProtocol.DiscoveryPort));
                    }
                    catch
                    {
                    }
                }

                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
                while (DateTime.UtcNow < deadline)
                {
                    Task<UdpReceiveResult> receiveTask = client.ReceiveAsync();
                    Task delayTask = Task.Delay(200);
                    Task completed = await Task.WhenAny(receiveTask, delayTask);
                    if (completed != receiveTask) continue;

                    UdpReceiveResult response = receiveTask.Result;
                    string text = Encoding.UTF8.GetString(response.Buffer);
                    if (!text.StartsWith(Reply + "|", StringComparison.Ordinal)) continue;

                    string[] parts = text.Split('|');
                    string name = parts.Length > 1 ? parts[1] : "Servidor";
                    int port = FileTransferProtocol.DefaultTcpPort;
                    if (parts.Length > 2)
                    {
                        int parsedPort;
                        if (int.TryParse(parts[2], out parsedPort) && parsedPort > 0 && parsedPort <= 65535)
                        {
                            port = parsedPort;
                        }
                    }
                    string address = response.RemoteEndPoint.Address.ToString();
                    if (!results.ContainsKey(address))
                    {
                        results.Add(address, new DiscoveredServer { Name = name, Address = address, Port = port });
                    }
                }
            }
            return new List<DiscoveredServer>(results.Values);
        }

        private static IEnumerable<IPAddress> GetBroadcastAddresses()
        {
            var addresses = new List<IPAddress>();
            addresses.Add(IPAddress.Broadcast);

            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                IPInterfaceProperties properties = adapter.GetIPProperties();
                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    IPAddress mask = unicast.IPv4Mask;
                    if (mask == null) continue;

                    byte[] ipBytes = unicast.Address.GetAddressBytes();
                    byte[] maskBytes = mask.GetAddressBytes();
                    byte[] broadcast = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        broadcast[i] = (byte)(ipBytes[i] | (maskBytes[i] ^ 255));
                    }
                    IPAddress broadcastAddress = new IPAddress(broadcast);
                    if (!addresses.Contains(broadcastAddress)) addresses.Add(broadcastAddress);
                }
            }

            return addresses;
        }

        private async Task RespondLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult packet = await _listener.ReceiveAsync();
                    string text = Encoding.UTF8.GetString(packet.Buffer);
                    if (text != Probe) continue;

                    string response = Reply + "|" + _name + "|" + _tcpPort;
                    byte[] bytes = Encoding.UTF8.GetBytes(response);
                    await _listener.SendAsync(bytes, bytes.Length, packet.RemoteEndPoint);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch
                {
                    if (token.IsCancellationRequested) return;
                }
            }
        }

        public void Dispose()
        {
            StopResponder();
            if (_cts != null) _cts.Dispose();
        }
    }
}

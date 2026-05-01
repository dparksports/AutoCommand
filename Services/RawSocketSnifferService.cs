using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AutoCommand.Models;

namespace AutoCommand.Services
{
    public class RawSocketSnifferService
    {
        private readonly ConcurrentDictionary<string, SvchostMonitorItem> _trackedIps;
        private CancellationTokenSource _cts;
        private Socket _mainSocket;

        public event Action<string> OnError;

        public RawSocketSnifferService(ConcurrentDictionary<string, SvchostMonitorItem> trackedIps)
        {
            _trackedIps = trackedIps;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();

            Task.Run(() =>
            {
                try
                {
                    IPAddress localIp = GetLocalIpAddress();
                    if (localIp == null)
                    {
                        OnError?.Invoke("Could not determine local IP address for raw socket binding.");
                        return;
                    }

                    _mainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                    _mainSocket.Bind(new IPEndPoint(localIp, 0));
                    _mainSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);

                    // RCVALL_ON
                    byte[] inValue = new byte[] { 1, 0, 0, 0 };
                    byte[] outValue = new byte[] { 0, 0, 0, 0 };
                    _mainSocket.IOControl(IOControlCode.ReceiveAll, inValue, outValue);

                    byte[] buffer = new byte[65535];

                    while (!_cts.Token.IsCancellationRequested)
                    {
                        if (_mainSocket.Available > 0)
                        {
                            int bytesRead = _mainSocket.Receive(buffer);
                            if (bytesRead >= 20) // Minimum IPv4 header length
                            {
                                ProcessPacket(buffer, bytesRead);
                            }
                        }
                        else
                        {
                            Thread.Sleep(1); // Prevent 100% CPU loop
                        }
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
                {
                    OnError?.Invoke("Raw socket binding failed: Access Denied. Ensure the application is running as Administrator.");
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Raw socket sniffer error: {ex.Message}");
                }
                finally
                {
                    _mainSocket?.Close();
                }
            }, _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _mainSocket?.Close();
        }

        private void ProcessPacket(byte[] buffer, int length)
        {
            try
            {
                // IPv4 header addresses are at offset 12 (src) and 16 (dst)
                string srcIp = new IPAddress(new ReadOnlySpan<byte>(buffer, 12, 4)).ToString();
                string dstIp = new IPAddress(new ReadOnlySpan<byte>(buffer, 16, 4)).ToString();

                // If src IP is in tracked IPs (incoming to local)
                if (_trackedIps.TryGetValue(srcIp, out var srcItem))
                {
                    srcItem.RxPackets++;
                    srcItem.RxBytes += length;
                }

                // If dst IP is in tracked IPs (outgoing from local)
                if (_trackedIps.TryGetValue(dstIp, out var dstItem))
                {
                    dstItem.TxPackets++;
                    dstItem.TxBytes += length;
                }
            }
            catch { }
        }

        private static IPAddress GetLocalIpAddress()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530); // Connects to a dummy external IP to resolve routing table local IP
                var endPoint = socket.LocalEndPoint as IPEndPoint;
                return endPoint?.Address;
            }
            catch
            {
                // Fallback to DNS
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                        return ip;
                }
                return null;
            }
        }
    }
}

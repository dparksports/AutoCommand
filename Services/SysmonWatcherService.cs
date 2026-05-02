using System;
using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AutoCommand.Models;

namespace AutoCommand.Services
{
    public class SysmonWatcherService
    {
        private EventLogWatcher _watcher;
        private readonly ConcurrentDictionary<string, SvchostMonitorItem> _trackedIps;
        private readonly ConcurrentDictionary<string, string> _dnsCache = new ConcurrentDictionary<string, string>();
        
        // Same regex from monitor_ultimate.py
        private static readonly Regex KnownDomainsRegex = new Regex(
            @"(?i)(microsoft|windows|azure|msedge|trafficmanager|google|1e100|googleapis|bing|live|office|skype|msn|azureedge)",
            RegexOptions.Compiled);

        public event Action<string> OnError;
        public event Action<SvchostMonitorItem> OnNewConnectionTracked;

        private readonly ConcurrentBag<string> _targetProcesses = new ConcurrentBag<string> { "svchost.exe" };

        public SysmonWatcherService(ConcurrentDictionary<string, SvchostMonitorItem> trackedIps)
        {
            _trackedIps = trackedIps;
        }

        public void AddTargetProcess(string processName)
        {
            if (!_targetProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
            {
                _targetProcesses.Add(processName);
            }
        }

        public void Start()
        {
            try
            {
                var query = new EventLogQuery("Microsoft-Windows-Sysmon/Operational", PathType.LogName, "*[System[(EventID=3)]]");
                _watcher = new EventLogWatcher(query);
                _watcher.EventRecordWritten += Watcher_EventRecordWritten;
                _watcher.Enabled = true;
            }
            catch (EventLogNotFoundException)
            {
                OnError?.Invoke("Sysmon is not installed. Please install Sysmon to use this feature.");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Failed to start Sysmon watcher: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (_watcher != null)
            {
                _watcher.Enabled = false;
                _watcher.Dispose();
                _watcher = null;
            }
        }

        private void Watcher_EventRecordWritten(object sender, EventRecordWrittenEventArgs e)
        {
            try
            {
                if (e.EventRecord == null) return;

                string xml = e.EventRecord.ToXml();
                var doc = XDocument.Parse(xml);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                var eventData = doc.Root?.Element(ns + "EventData");
                if (eventData == null) return;

                string image = string.Empty;
                string processId = string.Empty;
                string destIp = string.Empty;
                string destHost = string.Empty;

                foreach (var data in eventData.Elements(ns + "Data"))
                {
                    string name = data.Attribute("Name")?.Value;
                    if (name == "Image") image = data.Value;
                    else if (name == "ProcessId") processId = data.Value;
                    else if (name == "DestinationIp") destIp = data.Value;
                    else if (name == "DestinationHostname") destHost = data.Value;
                }

                // Monitor target processes
                bool isTarget = false;
                foreach (var target in _targetProcesses)
                {
                    if (image.EndsWith(target, StringComparison.OrdinalIgnoreCase))
                    {
                        isTarget = true;
                        break;
                    }
                }
                
                if (!isTarget) return;

                if (string.IsNullOrEmpty(destIp) || IsIgnoredIp(destIp) || IsKnownCloudIp(destIp)) return;

                if (string.IsNullOrEmpty(destHost) || destHost == "-")
                {
                    if (!_dnsCache.TryGetValue(destIp, out destHost))
                    {
                        try
                        {
                            var entry = Dns.GetHostEntry(destIp);
                            destHost = entry.HostName;
                        }
                        catch { destHost = "UNKNOWN"; }
                        
                        _dnsCache[destIp] = destHost;
                    }
                }

                if (!KnownDomainsRegex.IsMatch(destHost))
                {
                    if (!_trackedIps.ContainsKey(destIp))
                    {
                        var item = new SvchostMonitorItem
                        {
                            ProcessId = int.TryParse(processId, out int pid) ? pid : 0,
                            RemoteIp = destIp,
                            Hostname = destHost,
                            RxBytes = 0, TxBytes = 0, RxPackets = 0, TxPackets = 0
                        };

                        if (_trackedIps.TryAdd(destIp, item))
                        {
                            OnNewConnectionTracked?.Invoke(item);
                        }
                    }
                }
            }
            catch { }
        }

        private static bool IsIgnoredIp(string ipStr)
        {
            if (ipStr.StartsWith("127.") || ipStr == "::1") return true;
            if (ipStr.StartsWith("192.168.") || ipStr.StartsWith("10.")) return true;
            if (ipStr == "255.255.255.255") return true;
            
            var parts = ipStr.Split('.');
            if (parts.Length == 4)
            {
                if (parts[0] == "172" && int.TryParse(parts[1], out int p2) && p2 >= 16 && p2 <= 31)
                    return true;
                
                // Multicast 224.0.0.0 - 239.255.255.255
                if (int.TryParse(parts[0], out int p1) && p1 >= 224 && p1 <= 239)
                    return true;
            }
            
            return false;
        }

        private static readonly IPNetwork[] KnownCidrBlocks = {
            new IPNetwork(IPAddress.Parse("20.0.0.0"), 8),
            new IPNetwork(IPAddress.Parse("52.0.0.0"), 8),
            new IPNetwork(IPAddress.Parse("13.64.0.0"), 11),
            new IPNetwork(IPAddress.Parse("40.74.0.0"), 15),
            new IPNetwork(IPAddress.Parse("104.40.0.0"), 13),
            new IPNetwork(IPAddress.Parse("137.116.0.0"), 16),
            new IPNetwork(IPAddress.Parse("204.79.197.0"), 24),
            new IPNetwork(IPAddress.Parse("8.8.4.0"), 24),
            new IPNetwork(IPAddress.Parse("8.8.8.0"), 24),
            new IPNetwork(IPAddress.Parse("34.0.0.0"), 8),
            new IPNetwork(IPAddress.Parse("35.0.0.0"), 8),
            new IPNetwork(IPAddress.Parse("74.125.0.0"), 16),
            new IPNetwork(IPAddress.Parse("142.250.0.0"), 15),
            new IPNetwork(IPAddress.Parse("172.217.0.0"), 16),
            new IPNetwork(IPAddress.Parse("216.58.192.0"), 19)
        };

        private static bool IsKnownCloudIp(string ipStr)
        {
            if (!IPAddress.TryParse(ipStr, out var ip)) return false;
            foreach (var net in KnownCidrBlocks)
            {
                if (net.Contains(ip)) return true;
            }
            return false;
        }
    }

    // Helper for CIDR math
    public class IPNetwork
    {
        private readonly uint _networkAddress;
        private readonly uint _subnetMask;

        public IPNetwork(IPAddress ipAddress, int prefixLength)
        {
            byte[] ipBytes = ipAddress.GetAddressBytes();
            if (BitConverter.IsLittleEndian) Array.Reverse(ipBytes);
            uint ip = BitConverter.ToUInt32(ipBytes, 0);

            _subnetMask = prefixLength == 0 ? 0 : 0xFFFFFFFF << (32 - prefixLength);
            _networkAddress = ip & _subnetMask;
        }

        public bool Contains(IPAddress ipAddress)
        {
            if (ipAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            byte[] ipBytes = ipAddress.GetAddressBytes();
            if (BitConverter.IsLittleEndian) Array.Reverse(ipBytes);
            uint ip = BitConverter.ToUInt32(ipBytes, 0);

            return (ip & _subnetMask) == _networkAddress;
        }
    }
}

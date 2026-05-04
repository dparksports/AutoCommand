using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using AutoCommand.Models;

namespace AutoCommand.Services
{
    public class SysmonWatcherService
    {
        private EventLogWatcher _watcher;
        private readonly ConcurrentDictionary<string, SvchostMonitorItem> _trackedIps;

        // DNS cache entry: hostname + the time it was resolved
        private readonly ConcurrentDictionary<string, (string Host, DateTime ResolvedAt)> _dnsCache
            = new ConcurrentDictionary<string, (string, DateTime)>();

        // Geo-IP cache: friendly org/country label per IP
        private readonly ConcurrentDictionary<string, (string Label, DateTime ResolvedAt)> _geoCache
            = new ConcurrentDictionary<string, (string, DateTime)>();

        // Shared HttpClient — never dispose; one instance for the lifetime of the service
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };

        // How long we trust a cached DNS or geo result
        private static readonly TimeSpan DnsCacheTtl = TimeSpan.FromMinutes(30);

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

                // Resolve process name from PID
                int pid = int.TryParse(processId, out int p) ? p : 0;
                string processName = ResolveProcessName(pid);

                if (_trackedIps.ContainsKey(destIp)) return; // Already tracked — DNS will update async

                // Fire async DNS resolution so we don't block the Sysmon event thread
                _ = Task.Run(async () =>
                {
                    string resolvedHost = await ResolveDnsAsync(destIp, destHost);

                    if (KnownDomainsRegex.IsMatch(resolvedHost)) return; // Filtered after resolution

                    var item = new SvchostMonitorItem
                    {
                        ProcessId = pid,
                        ProcessName = processName,
                        RemoteIp = destIp,
                        Hostname = resolvedHost,
                        RxBytes = 0, TxBytes = 0, RxPackets = 0, TxPackets = 0
                    };

                    if (_trackedIps.TryAdd(destIp, item))
                    {
                        OnNewConnectionTracked?.Invoke(item);
                    }
                });
            }
            catch { }
        }

        // -----------------------------------------------------------------------
        // Process name resolution
        // -----------------------------------------------------------------------

        /// <summary>
        /// Looks up the process name for a given PID.
        /// Returns empty string if the process has already exited or access is denied.
        /// </summary>
        private static string ResolveProcessName(int pid)
        {
            if (pid <= 0) return string.Empty;
            try
            {
                return Process.GetProcessById(pid).ProcessName;
            }
            catch
            {
                // Process has already exited, or we don't have access (e.g. System/Idle)
                return string.Empty;
            }
        }

        // -----------------------------------------------------------------------
        // Async DNS resolution with TTL-based caching
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns a hostname for <paramref name="destIp"/>.
        /// Priority: Sysmon-provided host → cache (if fresh) → reverse DNS lookup.
        /// "UNKNOWN" results are cached but expire after <see cref="DnsCacheTtl"/> so
        /// transient failures are automatically retried.
        /// </summary>
        private async Task<string> ResolveDnsAsync(string destIp, string sysmonHost)
        {
            // Sysmon already gave us a real hostname — cache it and return
            if (!string.IsNullOrEmpty(sysmonHost) && sysmonHost != "-")
            {
                _dnsCache[destIp] = (sysmonHost, DateTime.UtcNow);
                return sysmonHost;
            }

            // Check cache — return if the entry is still fresh
            if (_dnsCache.TryGetValue(destIp, out var cached))
            {
                bool stale = (DateTime.UtcNow - cached.ResolvedAt) > DnsCacheTtl;
                if (!stale)
                    return cached.Host;
                // Stale → fall through to a fresh lookup below
            }

            // Perform async reverse DNS lookup
            string resolved;
            try
            {
                var entry = await Dns.GetHostEntryAsync(destIp);
                resolved = string.IsNullOrEmpty(entry.HostName) ? destIp : entry.HostName;
            }
            catch
            {
                resolved = destIp; // DNS failed — will try geo-IP below
            }

            // If DNS only gave us the raw IP back, enrich with geo-IP org + country
            // so non-technical users see e.g. "Comcast Cable · US" instead of "203.45.67.89"
            if (resolved == destIp)
            {
                string geoLabel = await GeoIpLookupAsync(destIp);
                if (!string.IsNullOrEmpty(geoLabel))
                    resolved = geoLabel;
            }

            _dnsCache[destIp] = (resolved, DateTime.UtcNow);
            return resolved;
        }

        // -----------------------------------------------------------------------
        // Geo-IP lookup (ip-api.com — free, no key, 1 000 req/min)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Calls ip-api.com to get the ISP/org and country for an IP address.
        /// Returns a friendly label like "Comcast Cable Communications · US",
        /// or an empty string if the lookup fails.
        /// Results are cached for <see cref="DnsCacheTtl"/> to avoid hammering the API.
        /// </summary>
        private async Task<string> GeoIpLookupAsync(string ip)
        {
            // Check geo cache first
            if (_geoCache.TryGetValue(ip, out var geo))
            {
                if ((DateTime.UtcNow - geo.ResolvedAt) < DnsCacheTtl)
                    return geo.Label;
            }

            try
            {
                // Fields: org (includes AS + ISP name) and country code
                string url = $"http://ip-api.com/json/{Uri.EscapeDataString(ip)}?fields=status,org,country,countryCode";
                string json = await _http.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("status", out var status) && status.GetString() == "success")
                {
                    string org         = root.TryGetProperty("org",         out var o) ? o.GetString() ?? string.Empty : string.Empty;
                    string countryCode = root.TryGetProperty("countryCode", out var c) ? c.GetString() ?? string.Empty : string.Empty;

                    // Strip leading "AS12345 " from the org name if present
                    if (org.Length > 0 && org[0] == 'A' && org[1] == 'S')
                    {
                        int space = org.IndexOf(' ');
                        if (space > 0) org = org[(space + 1)..];
                    }

                    string label = string.IsNullOrEmpty(countryCode)
                        ? org
                        : $"{org} · {countryCode}";

                    _geoCache[ip] = (label, DateTime.UtcNow);
                    return label;
                }
            }
            catch { /* network unavailable, API down, etc. — silently skip */ }

            // Cache an empty result so we don't hammer the API on every packet
            _geoCache[ip] = (string.Empty, DateTime.UtcNow);
            return string.Empty;
        }

        // -----------------------------------------------------------------------
        // IP filtering helpers
        // -----------------------------------------------------------------------

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

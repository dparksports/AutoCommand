using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AutoCommand.Helpers;
using AutoCommand.Models;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class ConnectionsView : UserControl, IAiAuditable
    {
        private List<NetworkConnectionItem> _allConnections = new();
        private readonly System.Collections.Concurrent.ConcurrentBag<NetworkConnectionItem> _eventConnections = new();
        private DispatcherTimer _liveTimer;
        private SysmonWatcherService _sysmonService;
        private RawSocketSnifferService _snifferService;
        private DnsResolutionService _dnsService;
        private bool _isSysmonActive = false;
        private bool _isSnifferActive = false;

        public ConnectionsView()
        {
            InitializeComponent();
            _dnsService = new DnsResolutionService();
            _dnsService.Start();
            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _liveTimer.Tick += async (s, e) => await LoadConnections();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (SourceCombo.SelectedIndex == 0)
                await LoadConnections();
        }

        private void SourceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceCombo == null || _liveTimer == null) return;

            if (SourceCombo.SelectedIndex == 1) // Sysmon
            {
                StopSniffer();
                StartSysmon();
                _liveTimer.Interval = TimeSpan.FromSeconds(1);
            }
            else if (SourceCombo.SelectedIndex == 2) // Raw Socket
            {
                StopSysmon();
                StartSniffer();
                _liveTimer.Interval = TimeSpan.FromSeconds(1);
            }
            else // Polling
            {
                StopSysmon();
                StopSniffer();
                _liveTimer.Interval = TimeSpan.FromSeconds(2);
            }

            if (LiveViewCheck != null && LiveViewCheck.IsChecked == true)
            {
                _liveTimer.Stop();
                _liveTimer.Start();
            }
        }

        private void StartSysmon()
        {
            if (_isSysmonActive) return;

            try
            {
                var trackedIps = new System.Collections.Concurrent.ConcurrentDictionary<string, SvchostMonitorItem>();
                var dnsService = new DnsResolutionService(); // Just for constructor
                _sysmonService = new SysmonWatcherService(trackedIps, dnsService);
                _sysmonService.MonitorAllProcesses = true;
                _sysmonService.OnNewConnectionTracked += (item) =>
                {
                    _eventConnections.Add(new NetworkConnectionItem
                    {
                        Protocol = item.Protocol ?? "EVENT",
                        LocalAddress = "local",
                        RemoteAddress = item.RemoteIp,
                        State = "ESTABLISHED",
                        ProcessId = item.ProcessId,
                        ProcessName = item.ProcessName
                    });
                };
                _sysmonService.Start();
                _isSysmonActive = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start Sysmon watcher: {ex.Message}");
                SourceCombo.SelectedIndex = 0;
            }
        }

        private void StartSniffer()
        {
            if (_isSnifferActive) return;
            try
            {
                var trackedIps = new System.Collections.Concurrent.ConcurrentDictionary<string, SvchostMonitorItem>();
                _snifferService = new RawSocketSnifferService(trackedIps);
                // Note: RawSocketSnifferService currently only updates stats for existing IPs.
                // It's primarily used in SvchostMonitorView where IPs are discovered via Sysmon.
                _snifferService.Start();
                _isSnifferActive = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start sniffer: {ex.Message}");
                SourceCombo.SelectedIndex = 0;
            }
        }

        private void StopSysmon()
        {
            _sysmonService?.Stop();
            _isSysmonActive = false;
        }

        private void StopSniffer()
        {
            _snifferService?.Stop();
            _isSnifferActive = false;
        }

        private void LiveViewChanged(object sender, RoutedEventArgs e)
        {
            if (LiveViewCheck.IsChecked == true)
                _liveTimer.Start();
            else
                _liveTimer.Stop();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await LoadConnections();

        private void FilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

        private async Task LoadConnections()
        {
            if (SourceCombo.SelectedIndex == 0) // Polling
            {
                _allConnections.Clear();

                await Task.Run(() =>
                {
                    try
                    {
                        var tcp = NetworkApiHelper.GetActiveTcpConnections();
                        var udp = NetworkApiHelper.GetActiveUdpConnections();
                        _allConnections.AddRange(tcp);
                        _allConnections.AddRange(udp);
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => MessageBox.Show($"Failed to load connections: {ex.Message}"));
                    }
                });
            }
            else if (SourceCombo.SelectedIndex == 1) // Sysmon
            {
                _allConnections = _eventConnections.ToList();
            }
            else // Raw Socket (Placeholder for more complex integration)
            {
                _allConnections = _eventConnections.ToList(); // Share event buffer for now
            }

            // Trigger DNS resolution for newly loaded IPs
            foreach (var conn in _allConnections)
            {
                if (string.IsNullOrEmpty(conn.RemoteAddress)) continue;
                
                string cached = _dnsService.GetCachedHostname(conn.RemoteAddress);
                if (cached != null)
                {
                    conn.RemoteHost = cached;
                }
                else
                {
                    conn.RemoteHost = conn.RemoteAddress; // Default to raw IP while resolving
                    _dnsService.EnqueueForResolution(conn);
                }
            }

            ApplyFilter();
        }


        private void ApplyFilter()
        {
            if (TcpRadio == null || ConnectionsGrid == null || ConnectionCountText == null) return;
            IEnumerable<NetworkConnectionItem> filtered = _allConnections;

            if (TcpRadio.IsChecked == true)
                filtered = filtered.Where(c => c.Protocol.Contains("TCP"));
            else if (UdpRadio.IsChecked == true)
                filtered = filtered.Where(c => c.Protocol.Contains("UDP"));

            var list = filtered.OrderBy(c => c.ProcessName).ThenBy(c => c.Protocol).ToList();

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(list);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("ProcessName"));

            ConnectionsGrid.ItemsSource = view;
            ConnectionCountText.Text = $"{list.Count} connections " +
                $"({list.Count(c => c.Protocol.Contains("TCP"))} TCP, {list.Count(c => c.Protocol.Contains("UDP"))} UDP)";
        }

        private async void KillProcess_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionsGrid.SelectedItem is not NetworkConnectionItem item) return;

            if (item.ProcessId == 0)
            {
                MessageBox.Show("Cannot kill System/Idle process.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"Kill process '{item.ProcessName}' (PID {item.ProcessId})?\nThis forcefully terminates the application.",
                "Confirm Kill", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                var proc = Process.GetProcessById(item.ProcessId);
                proc.Kill();
                await Task.Delay(500); // Give it a moment to release ports
                await LoadConnections();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to kill process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BlockProcess_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionsGrid.SelectedItem is not NetworkConnectionItem item) return;

            if (item.ProcessId == 0)
            {
                MessageBox.Show("Cannot block System/Idle process.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"Block all network traffic for '{item.ProcessName}' (PID {item.ProcessId}) in Windows Firewall?",
                "Confirm Block", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                string path = null;
                try
                {
                    var proc = Process.GetProcessById(item.ProcessId);
                    path = proc.MainModule?.FileName;
                }
                catch
                {
                    // Fallback to WMI if MainModule fails (e.g. 32-bit app reading 64-bit proc)
                    path = GetProcessPathViaWmi(item.ProcessId);
                }

                if (string.IsNullOrEmpty(path))
                {
                    MessageBox.Show("Could not determine process path (access denied or process exited).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                await FirewallService.Instance.AddBlockRuleForAppAsync(path, $"AutoCommand Block - {item.ProcessName}");
                MessageBox.Show($"Successfully added Inbound and Outbound block rules for:\n{path}", "Blocked", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to block process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BlockRemoteIP_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionsGrid.SelectedItem is not NetworkConnectionItem item) return;

            string ip = item.RemoteAddress;
            if (!System.Net.IPAddress.TryParse(ip, out _))
            {
                MessageBox.Show($"'{ip}' is not a valid IP address.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"Block all traffic to remote IP '{ip}' (contacted by '{item.ProcessName}') in Windows Firewall?\n\n" +
                                "Four rules are created: TCP/UDP, inbound and outbound.",
                "Confirm Block", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                await FirewallService.Instance.AddBlockRuleForIpAsync(ip, $"AutoCommand IP Block - {ip}");
                MessageBox.Show($"Successfully blocked remote IP:\n{ip}\n\n(TCP/UDP, inbound and outbound)", "Blocked", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to block remote IP: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BlockAllRemoteIPs_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionsGrid.SelectedItem is not NetworkConnectionItem item) return;

            var ips = _allConnections
                .Where(c => c.ProcessId == item.ProcessId)
                .Select(c => c.RemoteAddress)
                .Where(a => System.Net.IPAddress.TryParse(a, out _))
                .Distinct()
                .ToList();

            if (ips.Count == 0)
            {
                MessageBox.Show("No valid remote IP addresses found for this process.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string list = string.Join("\n", ips);
            if (MessageBox.Show($"Block all {ips.Count} remote IP address(es) used by '{item.ProcessName}' (PID {item.ProcessId})?\n\n{list}",
                "Confirm Block", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            int blocked = 0;
            var errors = new List<string>();
            foreach (string ip in ips)
            {
                try
                {
                    await FirewallService.Instance.AddBlockRuleForIpAsync(ip, $"AutoCommand IP Block - {ip}");
                    blocked++;
                }
                catch (Exception ex) { errors.Add($"{ip}: {ex.Message}"); }
            }

            string message = $"Blocked {blocked} of {ips.Count} remote IP(s).";
            if (errors.Count > 0) message += "\n\nFailures:\n" + string.Join("\n", errors);
            MessageBox.Show(message, "Blocked", MessageBoxButton.OK, errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        private string GetProcessPathViaWmi(int processId)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher($"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {processId}");
                using var results = searcher.Get();
                foreach (System.Management.ManagementObject obj in results)
                {
                    return obj["ExecutablePath"]?.ToString();
                }
            }
            catch { }
            return null;
        }

        public string GetAuditContext()
        {
            if (_allConnections.Count == 0) return "No active network connections found.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Active Network Connections:");
            foreach (var item in _allConnections)
            {
                sb.AppendLine($"- Process: {item.ProcessName} (PID: {item.ProcessId}) | Protocol: {item.Protocol} | Local: {item.LocalAddress} | Remote: {item.RemoteAddress} | State: {item.State}");
            }
            return sb.ToString();
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AutoCommand.Helpers;
using AutoCommand.Models;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class SvchostMonitorView : UserControl, IAiAuditable
    {
        private readonly ConcurrentDictionary<string, SvchostMonitorItem> _trackedIps = new();
        private readonly ObservableCollection<SvchostMonitorItem> _uiCollection = new();
        
        private DnsResolutionService _dnsService;
        private SysmonWatcherService _sysmonService;
        private RawSocketSnifferService _snifferService;
        private readonly SysmonInstallerService _installerService = new();
        private bool _isMonitoring = false;
        private DispatcherTimer _saveTimer;
        private DispatcherTimer _lastSeenRefreshTimer;
        private readonly string _csvPath = "ultimate_autopilot_stats.csv";

        // Firewall blocks created from this monitor, persisted in blocked.txt
        private readonly List<BlockedEntry> _blockedEntries = new();
        private readonly string _blockedPath = "blocked.txt";

        // Scheduled-task attribution for taskhostw rows (who/what/why launched)
        private readonly TaskAttributionService _taskAttribution = new();

        public SvchostMonitorView()
        {
            InitializeComponent();
            SvchostGrid.ItemsSource = _uiCollection;
        }

        private bool _isInitialized = false;

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) return;
            _isInitialized = true;

            CheckSysmonStatus();

            _dnsService   = new DnsResolutionService();
            _sysmonService = new SysmonWatcherService(_trackedIps, _dnsService);
            _sysmonService.MonitorAllProcesses = true;
            _sysmonService.FilterKnownCloudIps = HideCloudCheck.IsChecked == true;
            _snifferService = new RawSocketSnifferService(_trackedIps);

            _sysmonService.OnError += ShowError;
            _sysmonService.OnNewConnectionTracked += AddToUi;

            _snifferService.OnError += ShowError;

            // Attribution errors are non-fatal (rows just lack task info)
            _taskAttribution.OnError += msg => { };
            _taskAttribution.Start();

            RefreshIgnoredUi();
            LoadBlockedEntries();
            RefreshBlockedUi();

            // Setup auto-save every 1 hour
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _saveTimer.Tick += (s, args) => SaveToCsv();

            // Refresh relative "Last Packet" column + DNS cache chip every 10 seconds
            _lastSeenRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _lastSeenRefreshTimer.Tick += (s, args) =>
            {
                foreach (var item in _uiCollection)
                    item.RefreshLastSeenDisplay();

                // Re-enqueue any rows still showing raw IPs (handles items loaded from CSV etc.)
                foreach (var item in _uiCollection)
                    _dnsService.EnqueueForResolution(item);

                // Fill in scheduled-task attribution as the event logs catch up
                foreach (var item in _uiCollection)
                    EnrichWithTaskInfo(item);

                DnsCacheChip.Text = $"DNS cache: {_dnsService.CacheCount} entries";
            };
            _lastSeenRefreshTimer.Start();

            // Start DNS service before Sysmon so first events can hit the cache
            _dnsService.Start();

            // Auto-start monitor on load
            ToggleMonitorBtn_Click(null, null);
        }

        private void CheckSysmonStatus()
        {
            if (!_installerService.IsSysmonInstalled())
            {
                SetupSysmonBtn.Visibility = Visibility.Visible;
                MonitorStatusText.Text = "Sysmon is not installed. Network tracking will not work.";
            }
            else
            {
                SetupSysmonBtn.Visibility = Visibility.Collapsed;
            }
        }

        private async void SetupSysmonBtn_Click(object sender, RoutedEventArgs e)
        {
            SetupSysmonBtn.IsEnabled = false;

            // Inconsistent state = leftovers of a failed install (stale event
            // manifest registration / stray Sysmon64.exe) that make every
            // install attempt fail. Offer a guided repair instead.
            if (_installerService.DetectInstallState() == SysmonInstallState.Inconsistent)
            {
                await RepairSysmonFlowAsync();
                return;
            }

            MonitorStatusText.Text = "Downloading and configuring Sysmon... please wait.";

            var result = await _installerService.InstallAndConfigureAsync();
            if (result.Success)
            {
                MessageBox.Show("Sysmon has been successfully installed and configured for network tracking.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                CheckSysmonStatus();
                MonitorStatusText.Text = "Sysmon ready. Click 'Start Monitor' to begin tracking.";
            }
            else
            {
                MessageBox.Show($"Failed to install Sysmon.\n\nDetails: {result.ErrorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SetupSysmonBtn.IsEnabled = true;
                MonitorStatusText.Text = "Sysmon is not installed. Network tracking will not work.";
            }
        }

        private async Task RepairSysmonFlowAsync()
        {
            var repair = MessageBox.Show(
                "Sysmon is in an inconsistent install state: leftovers of an earlier failed install " +
                "(a stale event-manifest registration and/or a stray Sysmon64.exe) make every install attempt fail.\n\n" +
                "Repair now? The leftovers will be removed and Sysmon reinstalled. " +
                "If Windows still holds the stale registration, a restart will be offered to finish the repair.",
                "Sysmon repair", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);

            if (repair != MessageBoxResult.Yes)
            {
                SetupSysmonBtn.IsEnabled = true;
                MonitorStatusText.Text = "Sysmon repair cancelled. Network tracking will not work.";
                return;
            }

            MonitorStatusText.Text = "Repairing Sysmon... removing leftovers and reinstalling.";
            var repaired = await _installerService.RepairAsync();

            if (repaired.Success)
            {
                MessageBox.Show("Sysmon repair completed and verified. No restart was needed.", "Repair complete", MessageBoxButton.OK, MessageBoxImage.Information);
                CheckSysmonStatus();
                MonitorStatusText.Text = "Sysmon ready. Click 'Start Monitor' to begin tracking.";
                return;
            }

            if (repaired.RebootRequired)
            {
                var restart = MessageBox.Show(
                    "Windows still holds the stale Sysmon registration; it can only be cleared during a restart.\n\n" +
                    "Restart Windows now to finish the repair? Save your work first — the PC will reboot. " +
                    "After the restart AutoCommand will reinstall and verify Sysmon automatically.",
                    "Sysmon repair — restart required", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

                if (restart == MessageBoxResult.Yes)
                {
                    MonitorStatusText.Text = "Restarting Windows to complete the Sysmon repair...";
                    ProcessRunner.RunDetached("shutdown.exe",
                        "/r /t 10 /c \"AutoCommand: restarting to complete the Sysmon repair\"");
                    return;
                }

                MessageBox.Show("The repair will finish automatically after the next Windows restart: " +
                    "AutoCommand will reinstall and verify Sysmon on startup.", "Repair pending restart",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                MonitorStatusText.Text = "Sysmon repair pending restart. Network tracking will not work until then.";
                return;
            }

            MessageBox.Show($"Sysmon repair failed.\n\nDetails: {repaired.ErrorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            SetupSysmonBtn.IsEnabled = true;
            MonitorStatusText.Text = "Sysmon is not installed. Network tracking will not work.";
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (_sysmonService != null)
                _sysmonService.FilterKnownCloudIps = HideCloudCheck.IsChecked == true;
        }

        private void ShowError(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(msg, "Monitor Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                if (_isMonitoring) ToggleMonitorBtn_Click(null, null); // Stop on critical error
            });
        }

        private void AddToUi(SvchostMonitorItem item)
        {
            Dispatcher.Invoke(() =>
            {
                // The event may have raced with the user adding an ignore entry
                if (_sysmonService.IsProcessIgnored(item.ProcessName))
                {
                    _trackedIps.TryRemove(item.RemoteIp, out _);
                    return;
                }
                EnrichWithTaskInfo(item);
                _uiCollection.Add(item);
            });
        }

        /// <summary>
        /// Stamps scheduled-task attribution (task path + user) onto taskhostw
        /// rows once the event logs have caught up with the launch.
        /// </summary>
        private void EnrichWithTaskInfo(SvchostMonitorItem item)
        {
            if (item.TaskName != null) return;
            var info = _taskAttribution.GetTaskForPid(item.ProcessId);
            if (info == null || string.IsNullOrEmpty(info.TaskPath)) return;
            item.SetTaskInfo(info.TaskPath, info.UserContext);
        }

        private void ToggleMonitorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isMonitoring)
            {
                _sysmonService.Start();
                _snifferService.Start();
                _saveTimer.Start();
                
                ToggleMonitorBtn.Content = "Stop Monitor";
                MonitorStatusText.Text = "Monitor active: Sysmon event listening and Raw Socket sniffing enabled.";
                _isMonitoring = true;
            }
            else
            {
                _sysmonService.Stop();
                _snifferService.Stop();
                _saveTimer.Stop();
                
                ToggleMonitorBtn.Content = "Start Monitor";
                MonitorStatusText.Text = "Monitor is stopped.";
                _isMonitoring = false;
            }
        }

        private void AddProcessBtn_Click(object sender, RoutedEventArgs e)
        {
            string procName = NewProcessBox.Text.Trim();
            if (string.IsNullOrEmpty(procName)) return;

            // Ensure it ends with .exe for the internal filter
            if (!procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                procName += ".exe";
            }

            _sysmonService.AddTargetProcess(procName);
            NewProcessBox.Clear();
            MessageBox.Show($"Added {procName} to tracking list.", "Process Added", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // -----------------------------------------------------------------------
        // Process ignore list
        // -----------------------------------------------------------------------

        private void IgnoreProcessBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) IgnoreProcessBtn_Click(sender, e);
        }

        private void IgnoreProcessBtn_Click(object sender, RoutedEventArgs e)
        {
            string procName = IgnoreProcessBox.Text.Trim();
            if (string.IsNullOrEmpty(procName)) return;

            IgnoreProcess(procName);
            IgnoreProcessBox.Clear();
        }

        private void IgnoreRowMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (SvchostGrid.SelectedItem is SvchostMonitorItem item && !string.IsNullOrEmpty(item.ProcessName))
                IgnoreProcess(item.ProcessName);
        }

        private void RowMenu_Opened(object sender, RoutedEventArgs e)
        {
            var sel = SvchostGrid.SelectedItem as SvchostMonitorItem;

            IgnoreRowMenuItem.Header = sel != null && !string.IsNullOrEmpty(sel.ProcessName)
                ? $"🚫 Ignore process '{sel.ProcessName}'"
                : "🚫 Ignore process (no row selected)";

            bool validIp = sel != null && System.Net.IPAddress.TryParse(sel.RemoteIp, out _);
            BlockIpMenuItem.IsEnabled = validIp;
            BlockIpMenuItem.Header = validIp
                ? $"⛔ Block remote IP {sel.RemoteIp} in Firewall"
                : "⛔ Block Remote IP in Firewall";

            bool blockableProcess = sel != null && sel.ProcessId > 0;
            BlockProcessMenuItem.IsEnabled = blockableProcess;
            BlockProcessMenuItem.Header = blockableProcess && !string.IsNullOrEmpty(sel.ProcessName)
                ? $"⛔ Block process '{sel.ProcessName}' in Firewall"
                : "⛔ Block Process in Firewall";

            bool killable = sel != null && sel.ProcessId > 0;
            KillProcessMenuItem.IsEnabled = killable;
            KillProcessMenuItem.Header = killable && !string.IsNullOrEmpty(sel.ProcessName)
                ? $"💀 Kill process '{sel.ProcessName}' (PID {sel.ProcessId})"
                : "💀 Kill process";

            bool hasTask = sel != null && !string.IsNullOrEmpty(sel.TaskName);
            TaskDetailsMenuItem.IsEnabled = hasTask;
            TaskDetailsMenuItem.Header = hasTask
                ? $"📋 Scheduled task '{sel.TaskName}'"
                : "📋 Scheduled task details";
            DisableTaskMenuItem.IsEnabled = hasTask;
        }

        private void IgnoredListBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshIgnoredUi();
            IgnoredListPopup.IsOpen = true;
        }

        private void RemoveIgnoredBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoredListBox.SelectedItem is string name)
            {
                _sysmonService.RemoveIgnoredProcess(name);
                RefreshIgnoredUi();
            }
        }

        /// <summary>
        /// Hides a process name from the list: adds it to the persisted ignore
        /// list, then removes any rows already on screen for it.
        /// </summary>
        private void IgnoreProcess(string procName)
        {
            _sysmonService.AddIgnoredProcess(procName);
            PruneIgnoredRows();
            RefreshIgnoredUi();
        }

        /// <summary>Drops rows whose process is now ignored (list + packet counters).</summary>
        private void PruneIgnoredRows()
        {
            foreach (var item in _uiCollection.Where(i => _sysmonService.IsProcessIgnored(i.ProcessName)).ToList())
            {
                _uiCollection.Remove(item);
                _trackedIps.TryRemove(item.RemoteIp, out _);
            }
        }

        private void RefreshIgnoredUi()
        {
            var ignored = _sysmonService.IgnoredProcesses;
            IgnoredListBtn.Content = $"Ignored: {ignored.Count}";
            IgnoredListBox.ItemsSource = ignored.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void SvchostGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Right-click selects the row under the cursor so the "Ignore
            // process" menu item knows which process was meant.
            if (e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(SvchostGrid, source) is DataGridRow row)
            {
                row.IsSelected = true;
            }
        }

        // -----------------------------------------------------------------------
        // Firewall blocking (remote IP / process)
        // -----------------------------------------------------------------------

        /// <summary>A firewall block created from this monitor, persisted in blocked.txt.</summary>
        private sealed class BlockedEntry
        {
            public string Kind;         // "ip" or "proc"
            public string Key;          // remote IP or executable path
            public string ProcessName;
            public DateTime Timestamp;

            public override string ToString() => Kind == "ip"
                ? $"IP  {Key}  ({ProcessName}, blocked {Timestamp:yyyy-MM-dd HH:mm})"
                : $"EXE {Key}  ({ProcessName}, blocked {Timestamp:yyyy-MM-dd HH:mm})";
        }

        private async void BlockRemoteIP_Click(object sender, RoutedEventArgs e)
        {
            if (SvchostGrid.SelectedItem is not SvchostMonitorItem item) return;

            string ip = item.RemoteIp;
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
                RecordBlockedEntry("ip", ip, item.ProcessName);
                MessageBox.Show($"Successfully blocked remote IP:\n{ip}\n\n(TCP/UDP, inbound and outbound)", "Blocked", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to block remote IP: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BlockProcess_Click(object sender, RoutedEventArgs e)
        {
            if (SvchostGrid.SelectedItem is not SvchostMonitorItem item) return;

            if (item.ProcessId == 0)
            {
                MessageBox.Show("Cannot block System/Idle process.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"Block all network traffic for '{item.ProcessName}' (PID {item.ProcessId}) in Windows Firewall?",
                "Confirm Block", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                string path = GetProcessPath(item.ProcessId);
                if (string.IsNullOrEmpty(path))
                {
                    MessageBox.Show("Could not determine process path (access denied or process exited).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                await FirewallService.Instance.AddBlockRuleForAppAsync(path, $"AutoCommand Process Block - {item.ProcessName}");
                RecordBlockedEntry("proc", path, item.ProcessName);
                MessageBox.Show($"Successfully added Inbound and Outbound block rules for:\n{path}", "Blocked", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to block process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BlockedListBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshBlockedUi();
            BlockedListPopup.IsOpen = true;
        }

        private async void UnblockBtn_Click(object sender, RoutedEventArgs e)
        {
            if (BlockedListBox.SelectedItem is not BlockedEntry entry) return;

            if (MessageBox.Show($"Remove the firewall block for '{entry.Key}'?",
                "Confirm Unblock", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            try
            {
                int removed = entry.Kind == "ip"
                    ? await FirewallService.Instance.RemoveBlockRulesForIpAsync(entry.Key)
                    : await FirewallService.Instance.RemoveBlockRulesForAppAsync(entry.Key);

                _blockedEntries.Remove(entry);
                SaveBlockedEntries();
                RefreshBlockedUi();

                MessageBox.Show(removed > 0
                    ? $"Removed {removed} firewall rule(s) for:\n{entry.Key}"
                    : $"No matching firewall rules found (already removed?) for:\n{entry.Key}",
                    "Unblocked", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to unblock: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void KillProcess_Click(object sender, RoutedEventArgs e)
        {
            if (SvchostGrid.SelectedItem is not SvchostMonitorItem item) return;

            if (item.ProcessId == 0)
            {
                MessageBox.Show("Cannot kill System/Idle process.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var proc = Process.GetProcessById(item.ProcessId);

                // PID reuse guard: the row may be stale — the original process
                // exited and Windows recycled the PID to something else.
                // Verify the live identity before pulling the trigger.
                string liveName = string.Empty;
                try { liveName = proc.ProcessName; } catch { }

                string knownName = Path.GetFileNameWithoutExtension(item.ProcessName ?? string.Empty);
                string confirmName = item.ProcessName;
                if (knownName.Length > 0 && !liveName.Equals(knownName, StringComparison.OrdinalIgnoreCase))
                {
                    confirmName = liveName;
                    var stale = MessageBox.Show(
                        $"This row is stale: PID {item.ProcessId} now runs '{liveName}', not '{item.ProcessName}'.\n\n" +
                        $"Kill the running process '{liveName}' (PID {item.ProcessId}) anyway?\n" +
                        "This forcefully terminates the application.",
                        "PID reused — confirm kill", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (stale != MessageBoxResult.Yes) return;
                }
                else if (MessageBox.Show(
                    $"Kill process '{confirmName}' (PID {item.ProcessId})?\nThis forcefully terminates the application.",
                    "Confirm Kill", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }

                proc.Kill();
                await Task.Delay(500); // Give it a moment to release ports
            }
            catch (ArgumentException)
            {
                MessageBox.Show($"Process {item.ProcessId} is no longer running (it already exited).",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to kill process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TaskDetails_Click(object sender, RoutedEventArgs e)
        {
            if (SvchostGrid.SelectedItem is not SvchostMonitorItem item || string.IsNullOrEmpty(item.TaskName)) return;

            var (summary, error) = await TaskSchedulerService.Instance.GetTaskSummaryAsync(item.TaskName);
            MessageBox.Show(
                string.IsNullOrEmpty(error)
                    ? summary
                    : $"Could not read task '{item.TaskName}': {error}",
                "Scheduled task", MessageBoxButton.OK,
                string.IsNullOrEmpty(error) ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private async void DisableTask_Click(object sender, RoutedEventArgs e)
        {
            if (SvchostGrid.SelectedItem is not SvchostMonitorItem item || string.IsNullOrEmpty(item.TaskName)) return;

            if (MessageBox.Show(
                $"Disable scheduled task '{item.TaskName}'?\n\n" +
                "The task will not run again until it is re-enabled (Tasks tab, or:\n" +
                $"schtasks /Change /TN \"{item.TaskName}\" /ENABLE).\n\n" +
                "The running instance keeps going — use 'Kill process' to end it now.",
                "Confirm Disable Task", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                await TaskSchedulerService.Instance.SetTaskEnabledAsync(item.TaskName, false);
                MessageBox.Show($"Scheduled task disabled:\n{item.TaskName}", "Task Disabled",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to disable task: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Resolves an executable path for a PID; falls back to WMI when the
        /// process module cannot be read (bitness mismatch) or the process exited.
        /// </summary>
        private static string GetProcessPath(int processId)
        {
            try
            {
                using var proc = Process.GetProcessById(processId);
                string path = proc.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path)) return path;
            }
            catch { }

            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {processId}");
                using var results = searcher.Get();
                foreach (System.Management.ManagementObject obj in results)
                {
                    return obj["ExecutablePath"]?.ToString();
                }
            }
            catch { }
            return null;
        }

        // -----------------------------------------------------------------------
        // blocked.txt persistence (format: kind|key|processName|timestamp)
        // -----------------------------------------------------------------------

        private void RecordBlockedEntry(string kind, string key, string processName)
        {
            // One record per key — re-blocking refreshes it instead of duplicating
            _blockedEntries.RemoveAll(b => b.Kind == kind && b.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            _blockedEntries.Add(new BlockedEntry { Kind = kind, Key = key, ProcessName = processName ?? "", Timestamp = DateTime.Now });
            SaveBlockedEntries();
            RefreshBlockedUi();
        }

        private void LoadBlockedEntries()
        {
            _blockedEntries.Clear();
            try
            {
                foreach (string line in File.ReadAllLines(_blockedPath))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length < 4) continue;
                    if (parts[0] != "ip" && parts[0] != "proc") continue;
                    _blockedEntries.Add(new BlockedEntry
                    {
                        Kind = parts[0],
                        Key = parts[1],
                        ProcessName = parts[2],
                        Timestamp = DateTime.TryParse(parts[3], out DateTime ts) ? ts : DateTime.Now
                    });
                }
            }
            catch { /* first run or unreadable file — start with an empty block list */ }
        }

        private void SaveBlockedEntries()
        {
            try
            {
                File.WriteAllLines(_blockedPath, _blockedEntries.Select(b => $"{b.Kind}|{b.Key}|{b.ProcessName}|{b.Timestamp:O}"));
            }
            catch { /* persistence is best-effort — the firewall rules themselves are the source of truth */ }
        }

        private void RefreshBlockedUi()
        {
            BlockedListBtn.Content = $"Blocked: {_blockedEntries.Count}";
            BlockedListBox.ItemsSource = _blockedEntries
                .OrderBy(b => b.Kind).ThenBy(b => b.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void SaveCsvBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveToCsv();
            MessageBox.Show($"Data saved to {_csvPath}", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveToCsv()
        {
            try
            {
                bool writeHeader = !File.Exists(_csvPath);
                using var writer = new StreamWriter(_csvPath, true);
                
                if (writeHeader)
                {
                    writer.WriteLine("Timestamp,PID,ProcessName,RemoteIP,Host,RxPackets,TxPackets,RxBytes,TxBytes,LastSeen");
                }

                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var snap = _trackedIps.Values.ToList();
                
                foreach (var item in snap)
                {
                    writer.WriteLine($"{ts},{item.ProcessId},{item.ProcessName},{item.RemoteIp},{item.Hostname},{item.RxPackets},{item.TxPackets},{item.RxBytes},{item.TxBytes},{item.LastSeen:u}");
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show($"Failed to save CSV: {ex.Message}"));
            }
        }

        public string GetAuditContext()
        {
            var items = _uiCollection.ToList();
            if (items.Count == 0) return "Process Monitor is currently empty. No active connections tracked.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Currently Tracked Processes and Connections:");
            foreach (var item in items)
            {
                sb.AppendLine($"- Process: {item.ProcessName} (PID: {item.ProcessId}) | Remote IP: {item.RemoteIp} ({item.Hostname}) | Packets: {item.TxPackets} sent, {item.RxPackets} received | Last Seen: {item.LastSeen:u}");
            }
            return sb.ToString();
        }
    }
}

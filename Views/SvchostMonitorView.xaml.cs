using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
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
            Dispatcher.Invoke(() => _uiCollection.Add(item));
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

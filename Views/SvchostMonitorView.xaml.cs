using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AutoCommand.Models;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class SvchostMonitorView : UserControl
    {
        private readonly ConcurrentDictionary<string, SvchostMonitorItem> _trackedIps = new();
        private readonly ObservableCollection<SvchostMonitorItem> _uiCollection = new();
        
        private SysmonWatcherService _sysmonService;
        private RawSocketSnifferService _snifferService;
        private bool _isMonitoring = false;
        private DispatcherTimer _saveTimer;
        private readonly string _csvPath = "ultimate_autopilot_stats.csv";

        public SvchostMonitorView()
        {
            InitializeComponent();
            SvchostGrid.ItemsSource = _uiCollection;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _sysmonService = new SysmonWatcherService(_trackedIps);
            _snifferService = new RawSocketSnifferService(_trackedIps);

            _sysmonService.OnError += ShowError;
            _sysmonService.OnNewConnectionTracked += AddToUi;
            
            _snifferService.OnError += ShowError;

            // Setup auto-save every 1 hour
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _saveTimer.Tick += (s, args) => SaveToCsv();

            // Auto-start monitor on load
            ToggleMonitorBtn_Click(null, null);
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
                    writer.WriteLine("Timestamp,PID,RemoteIP,Host,RxPackets,TxPackets,RxBytes,TxBytes");
                }

                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var snap = _trackedIps.Values.ToList();
                
                foreach (var item in snap)
                {
                    writer.WriteLine($"{ts},{item.ProcessId},{item.RemoteIp},{item.Hostname},{item.RxPackets},{item.TxPackets},{item.RxBytes},{item.TxBytes}");
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show($"Failed to save CSV: {ex.Message}"));
            }
        }
    }
}

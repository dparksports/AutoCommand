using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace AutoCommand
{
    public partial class MainWindow : Window
    {
        private SecurityEnforcer _enforcer;

        public MainWindow()
        {
            InitializeComponent();
            InitializeEnforcer();
            
            // Fire telemetry app_open event (fire-and-forget)
            _ = Services.AnalyticsService.Instance.TrackEventAsync("app_open", new Dictionary<string, object>
            {
                { "app_version", "3.2" }
            });
        }

        private void InitializeEnforcer()
        {
            _enforcer = new SecurityEnforcer(OnThreatDetected);
            _enforcer.StatusChanged += OnEnforcerStatusChanged;
            _enforcer.ConfigurationDriftDetected += OnDriftDetected;
            _enforcer.Start();
        }

        private string _currentThreatType;
        private string _currentThreatDetails;

        private void OnThreatDetected(string type, string details)
        {
            Dispatcher.Invoke(() =>
            {
                _currentThreatType = type;
                _currentThreatDetails = details;
                ThreatAlertText.Text = $"⚠ {type}: {details}";
                ThreatAlertText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31));
                ReviewAlertBtn.Visibility = Visibility.Visible;
            });
        }

        private void ReviewAlertBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentThreatType == "Privileged Task")
            {
                string msg = $"{_currentThreatDetails}\n\n" +
                             "This task is running with the highest system privileges. While some third-party updaters (like Edge or Chrome) do this legitimately, malware often uses it for persistence.\n\n" +
                             "Would you like to ignore this task permanently (Whitelist)?\n" +
                             "Click 'No' to keep alerting, or go to the 'Tasks' tab to manually investigate and delete it.";
                
                var result = MessageBox.Show(msg, "Review Threat", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    // Extract task name and whitelist it
                    string taskName = _currentThreatDetails.Replace("High Risk Task detected: ", "").Trim();
                    SecurityEnforcer.WhitelistTask(taskName);
                    ThreatAlertText.Text = "✓ Task added to whitelist.";
                    ThreatAlertText.Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F));
                    ReviewAlertBtn.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                MessageBox.Show($"{_currentThreatType}\n\n{_currentThreatDetails}\n\nPlease check the Command Panel for more details.", "Security Alert", MessageBoxButton.OK, MessageBoxImage.Information);
                ReviewAlertBtn.Visibility = Visibility.Collapsed;
            }
        }

        private void OnEnforcerStatusChanged(string status, string colorType)
        {
            Dispatcher.Invoke(() =>
            {
                EnforcerStatusText.Text = $"Security Enforcer: {status}";
                EnforcerDot.Fill = colorType switch
                {
                    "Red" => new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B)),
                    "Amber" => new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31)),
                    _ => new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F))
                };
            });
        }

        private void OnDriftDetected(List<string> driftItems)
        {
            Dispatcher.Invoke(() =>
            {
                ThreatAlertText.Text = $"⚠ Firewall drift detected: {driftItems.Count} rule(s) changed";
                ThreatAlertText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31));
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _enforcer?.Stop();
            base.OnClosed(e);
        }
    }
}


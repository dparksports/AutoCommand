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
        }

        private void InitializeEnforcer()
        {
            _enforcer = new SecurityEnforcer(OnThreatDetected);
            _enforcer.StatusChanged += OnEnforcerStatusChanged;
            _enforcer.ConfigurationDriftDetected += OnDriftDetected;
            _enforcer.Start();
        }

        private void OnThreatDetected(string type, string details)
        {
            Dispatcher.Invoke(() =>
            {
                ThreatAlertText.Text = $"⚠ {type}: {details}";
                ThreatAlertText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31));
            });
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


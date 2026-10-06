using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Services;
using AutoCommand.Helpers;

namespace AutoCommand.Views
{
    public partial class SettingsView : UserControl
    {
        private const string TaskName = FreshSetupService.StartupTaskName;

        public SettingsView()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckStartupTask();
            
            // Load Analytics preference and sync both telemetry services
            AnalyticsCheck.Checked -= AnalyticsCheck_Checked;
            AnalyticsCheck.Unchecked -= AnalyticsCheck_Unchecked;
            bool analyticsEnabled = AnalyticsService.Instance.IsAnalyticsEnabled;
            AnalyticsCheck.IsChecked = analyticsEnabled;
            TelemetryService.Instance.ConsentGranted = analyticsEnabled;
            AnalyticsCheck.Checked += AnalyticsCheck_Checked;
            AnalyticsCheck.Unchecked += AnalyticsCheck_Unchecked;

            // Load Auto-Mitigate Adapters preference
            AutoMitigateAdaptersCheck.Checked -= AutoMitigateAdaptersCheck_Checked;
            AutoMitigateAdaptersCheck.Unchecked -= AutoMitigateAdaptersCheck_Unchecked;
            AutoMitigateAdaptersCheck.IsChecked = SecurityEnforcer.AutoMitigateAdapters;
            AutoMitigateAdaptersCheck.Checked += AutoMitigateAdaptersCheck_Checked;
            AutoMitigateAdaptersCheck.Unchecked += AutoMitigateAdaptersCheck_Unchecked;

            // Load the security check rate (fast-check tick interval)
            CheckRateCombo.SelectionChanged -= CheckRateCombo_SelectionChanged;
            foreach (var item in CheckRateCombo.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is string tag && tag == SecurityEnforcer.CheckIntervalSeconds.ToString())
                {
                    item.IsSelected = true;
                    break;
                }
            }
            CheckRateCombo.SelectionChanged += CheckRateCombo_SelectionChanged;
        }

        private void CheckRateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CheckRateCombo.SelectedItem is ComboBoxItem item
                && item.Tag is string tag && int.TryParse(tag, out int seconds))
            {
                SecurityEnforcer.CheckIntervalSeconds = seconds;
            }
        }

        private void AnalyticsCheck_Checked(object sender, RoutedEventArgs e)
        {
            AnalyticsService.Instance.IsAnalyticsEnabled = true;
            TelemetryService.Instance.ConsentGranted = true;
        }

        private void AnalyticsCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            AnalyticsService.Instance.IsAnalyticsEnabled = false;
            TelemetryService.Instance.ConsentGranted = false;
        }

        private void AutoMitigateAdaptersCheck_Checked(object sender, RoutedEventArgs e)
        {
            SecurityEnforcer.AutoMitigateAdapters = true;
        }

        private void AutoMitigateAdaptersCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            SecurityEnforcer.AutoMitigateAdapters = false;
        }

        private async Task CheckStartupTask()
        {
            bool exists = false;
            await Task.Run(() =>
            {
                try
                {
                    Type tsType = Type.GetTypeFromProgID("Schedule.Service");
                    dynamic ts = Activator.CreateInstance(tsType);
                    ts.Connect();
                    dynamic rootFolder = ts.GetFolder("\\");
                    dynamic task = rootFolder.GetTask(TaskName);
                    exists = task != null;
                }
                catch
                {
                    exists = false;
                }
            });

            // Detach events to avoid infinite loop
            LaunchOnStartupCheck.Checked -= LaunchOnStartupCheck_Checked;
            LaunchOnStartupCheck.Unchecked -= LaunchOnStartupCheck_Unchecked;
            
            LaunchOnStartupCheck.IsChecked = exists;

            LaunchOnStartupCheck.Checked += LaunchOnStartupCheck_Checked;
            LaunchOnStartupCheck.Unchecked += LaunchOnStartupCheck_Unchecked;
        }

        private async void LaunchOnStartupCheck_Checked(object sender, RoutedEventArgs e)
        {
            bool ok = await FreshSetupService.EnableLaunchAtLogon();
            if (!ok)
                MessageBox.Show("Failed to create startup task.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            await CheckStartupTask();
        }

        private async void LaunchOnStartupCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            await FreshSetupService.DisableLaunchAtLogon();
            await CheckStartupTask();
        }
    }
}
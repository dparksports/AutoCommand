using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Services;
using AutoCommand.Helpers;

namespace AutoCommand.Views
{
    public partial class SettingsView : UserControl
    {
        private const string TaskName = "AutoCommandStartupTask";

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
            await Task.Run(() =>
            {
                try
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    // Use schtasks to create a highest privilege task on logon
                    ProcessRunner.Run("schtasks", $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f");
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => MessageBox.Show($"Failed to create startup task: {ex.Message}"));
                }
            });
        }

        private async void LaunchOnStartupCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                try
                {
                    ProcessRunner.Run("schtasks", $"/delete /tn \"{TaskName}\" /f");
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => MessageBox.Show($"Failed to remove startup task: {ex.Message}"));
                }
            });
        }
    }
}
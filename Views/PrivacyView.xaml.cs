using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using AutoCommand.Helpers;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class PrivacyView : UserControl
    {
        private static readonly string[] VpnServices = { "RasMan", "IKEEXT", "PolicyAgent", "RemoteAccess" };
        private const string UsageDataTaskPath = @"\Microsoft\Windows\Flighting\FeatureConfig\UsageDataReceiver";

        public PrivacyView()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckAllStatus();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await CheckAllStatus();

        private async Task CheckAllStatus()
        {
            await Task.WhenAll(
                CheckVpnStatus(),
                CheckWifiDirectStatus(),
                CheckKdnetStatus(),
                CheckUsageDataStatus(),
                CheckTamperProtection()
            );
        }

        // ── VPN Services ──
        private Task CheckVpnStatus()
        {
            return Task.Run(() =>
            {
                int running = 0, stopped = 0;
                foreach (var svcName in VpnServices)
                {
                    try
                    {
                        using var sc = new ServiceController(svcName);
                        if (sc.Status == ServiceControllerStatus.Running) running++;
                        else stopped++;
                    }
                    catch { stopped++; }
                }

                string status = running > 0
                    ? $"⚠ {running} service(s) running — potential attack surface"
                    : $"✓ All {VpnServices.Length} services stopped";

                Dispatcher.Invoke(() =>
                {
                    VpnStatusText.Text = status;
                    VpnStatusText.Foreground = running > 0
                        ? new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31))
                        : new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F));
                });
            });
        }

        private async void VpnDisableBtn_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                foreach (var svc in VpnServices)
                    ServiceHelper.StopAndDisable(svc);
            });
            await CheckVpnStatus();
        }

        private async void VpnEnableBtn_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                foreach (var svc in VpnServices)
                    ServiceHelper.EnableAndStart(svc);
            });
            await CheckVpnStatus();
        }

        // ── WiFi Direct ──
        private Task CheckWifiDirectStatus()
        {
            return Task.Run(() =>
            {
                bool found = false;
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "SELECT * FROM Win32_NetworkAdapter WHERE Name LIKE '%Wi-Fi Direct%'");
                    found = searcher.Get().Count > 0;
                }
                catch { }

                Dispatcher.Invoke(() =>
                {
                    WifiDirectStatusText.Text = found ? "⚠ WiFi Direct adapter present" : "✓ No WiFi Direct adapters found";
                    WifiDirectStatusText.Foreground = found
                        ? new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31))
                        : new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F));
                });
            });
        }

        private async void WifiDirectDisableBtn_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "SELECT PNPDeviceID FROM Win32_NetworkAdapter WHERE Name LIKE '%Wi-Fi Direct%'");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string deviceId = obj["PNPDeviceID"]?.ToString();
                        if (!string.IsNullOrEmpty(deviceId))
                            ProcessRunner.RunDetached("pnputil.exe", $"/disable-device \"{deviceId}\"");
                    }
                }
                catch { }
            });
            await CheckWifiDirectStatus();
        }

        private async void WifiDirectEnableBtn_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "SELECT PNPDeviceID FROM Win32_NetworkAdapter WHERE Name LIKE '%Wi-Fi Direct%'");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string deviceId = obj["PNPDeviceID"]?.ToString();
                        if (!string.IsNullOrEmpty(deviceId))
                            ProcessRunner.RunDetached("pnputil.exe", $"/enable-device \"{deviceId}\"");
                    }
                }
                catch { }
            });
            await CheckWifiDirectStatus();
        }

        // ── KDNET ──
        private Task CheckKdnetStatus()
        {
            return Task.Run(() =>
            {
                string output = ProcessRunner.Run("bcdedit", "/enum {current}");
                bool debugOn = output.Contains("debug") && output.Contains("Yes");

                Dispatcher.Invoke(() =>
                {
                    KdnetStatusText.Text = debugOn ? "⚠ Kernel Debug is ENABLED" : "✓ Kernel Debug is disabled";
                    KdnetStatusText.Foreground = debugOn
                        ? new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B))
                        : new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F));
                });
            });
        }

        private async void KdnetDisableBtn_Click(object sender, RoutedEventArgs e)
        {
            await ProcessRunner.RunAsync("bcdedit", "/debug off");
            await CheckKdnetStatus();
        }

        private async void KdnetEnableBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Enable kernel debugging? This is a significant security risk.",
                "Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await ProcessRunner.RunAsync("bcdedit", "/debug on");
            await CheckKdnetStatus();
        }

        // ── Usage Data / Telemetry ──
        private Task CheckUsageDataStatus()
        {
            return Task.Run(() =>
            {
                bool enabled = TaskSchedulerService.Instance.IsTaskEnabled(UsageDataTaskPath);

                Dispatcher.Invoke(() =>
                {
                    UsageDataStatusText.Text = enabled
                        ? "⚠ Telemetry task is active — data being sent"
                        : "✓ Telemetry task is disabled";
                    UsageDataStatusText.Foreground = enabled
                        ? new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31))
                        : new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F));
                });
            });
        }

        private async void UsageDataDisableBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await TaskSchedulerService.Instance.SetTaskEnabledAsync(UsageDataTaskPath, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            await CheckUsageDataStatus();
        }

        private async void UsageDataEnableBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await TaskSchedulerService.Instance.SetTaskEnabledAsync(UsageDataTaskPath, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            await CheckUsageDataStatus();
        }

        // ── Tamper Protection ──
        private Task CheckTamperProtection()
        {
            return Task.Run(() =>
            {
                bool isProtected = false;
                try
                {
                    // Native C# Registry read — replaces PowerShell registry query
                    object value = Registry.GetValue(
                        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Defender\Features",
                        "TamperProtection", null);
                    isProtected = value != null && Convert.ToInt32(value) == 5;
                }
                catch { }

                Dispatcher.Invoke(() =>
                {
                    TamperStatusText.Text = isProtected
                        ? "✓ Tamper Protection is ON"
                        : "⚠ Tamper Protection is OFF — settings may be vulnerable";
                    TamperStatusText.Foreground = isProtected
                        ? new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F))
                        : new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B));
                    TamperIcon.Text = isProtected ? "🛡" : "⚠";
                });
            });
        }
    }
}


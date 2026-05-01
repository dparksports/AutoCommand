using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoCommand.Helpers;

namespace AutoCommand.Views
{
    public partial class HardeningView : UserControl
    {
        private const string HostsFilePath = @"C:\Windows\System32\drivers\etc\hosts";
        
        public HardeningView()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshAll();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await RefreshAll();

        private async Task RefreshAll()
        {
            await Task.WhenAll(
                CheckLsaStatus(),
                CheckUacStatus(),
                CheckHostsFile()
            );
        }

        // ── LSA Protection ──
        private Task CheckLsaStatus()
        {
            return Task.Run(() =>
            {
                bool isProtected = RegistryHelper.GetLsaProtectionStatus();
                Dispatcher.Invoke(() =>
                {
                    LsaStatusText.Text = isProtected ? "✓ LSA Protection (RunAsPPL) is ENABLED" : "⚠ LSA Protection is DISABLED";
                    LsaStatusText.Foreground = isProtected
                        ? new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F))
                        : new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31));
                });
            });
        }

        private async void LsaEnableBtn_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => RegistryHelper.SetLsaProtectionStatus(true));
            await CheckLsaStatus();
            MessageBox.Show("LSA Protection enabled. A reboot is required for this to take effect.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void LsaDisableBtn_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => RegistryHelper.SetLsaProtectionStatus(false));
            await CheckLsaStatus();
        }

        // ── UAC Strictness ──
        private Task CheckUacStatus()
        {
            return Task.Run(() =>
            {
                bool isStrict = RegistryHelper.GetUacStrictness();
                Dispatcher.Invoke(() =>
                {
                    UacStatusText.Text = isStrict ? "✓ UAC is at Maximum Strictness" : "⚠ UAC is relaxed or disabled";
                    UacStatusText.Foreground = isStrict
                        ? new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F))
                        : new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31));
                });
            });
        }

        private async void UacStrictBtn_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => RegistryHelper.SetUacStrictness(true));
            await CheckUacStatus();
        }

        private async void UacRelaxBtn_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => RegistryHelper.SetUacStrictness(false));
            await CheckUacStatus();
        }

        // ── Hosts File ──
        private Task CheckHostsFile()
        {
            return Task.Run(() =>
            {
                try
                {
                    if (File.Exists(HostsFilePath))
                    {
                        string content = File.ReadAllText(HostsFilePath);
                        
                        // Check for suspicious non-comment lines
                        var lines = File.ReadAllLines(HostsFilePath);
                        int activeRules = 0;
                        bool suspicious = false;
                        foreach(var line in lines)
                        {
                            var t = line.Trim();
                            if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
                            activeRules++;
                            if (!t.Contains("localhost") && !t.Contains("127.0.0.1") && !t.Contains("::1"))
                            {
                                suspicious = true;
                            }
                        }

                        Dispatcher.Invoke(() =>
                        {
                            HostsContentBox.Text = content;
                            if (suspicious)
                            {
                                HostsStatusText.Text = $"⚠ Found {activeRules} active redirect(s). Potentially suspicious.";
                                HostsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B));
                            }
                            else if (activeRules > 0)
                            {
                                HostsStatusText.Text = $"✓ Found {activeRules} standard redirect(s).";
                                HostsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F));
                            }
                            else
                            {
                                HostsStatusText.Text = "✓ Hosts file is clean (default).";
                                HostsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F));
                            }
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            HostsContentBox.Text = "(File not found)";
                            HostsStatusText.Text = "✓ Hosts file missing (default behavior on some systems).";
                            HostsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F));
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => HostsStatusText.Text = $"Error reading hosts: {ex.Message}");
                }
            });
        }

        private async void HostsResetBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Reset hosts file to Windows default? All custom entries will be lost.",
                "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            await Task.Run(() =>
            {
                try
                {
                    string defaultContent = @"# Copyright (c) 1993-2009 Microsoft Corp.
#
# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.
#
# 127.0.0.1       localhost
# ::1             localhost
";
                    File.WriteAllText(HostsFilePath, defaultContent);
                }
                catch { }
            });
            await CheckHostsFile();
        }
    }
}

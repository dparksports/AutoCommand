using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Helpers;
using AutoCommand.Models;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class CommandPanelView : UserControl, IAiAuditable
    {
        public CommandPanelView()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshAll();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAll();
        }

        private async Task RefreshAll()
        {
            await Task.WhenAll(
                LoadSstpStatus(),
                LoadWifiDirectStatus(),
                LoadKdnetStatus(),
                LoadWanMiniports(),
                LoadWifiDirectAdapters(),
                LoadKdnetAdapters()
            );

            // Sync button label to actual service state after any refresh
            UpdateSstpButtonLabel();
        }

        private void UpdateSstpButtonLabel()
        {
            try
            {
                using var sc = new ServiceController("SstpSvc");
                bool active = sc.Status == ServiceControllerStatus.Running
                           || sc.Status == ServiceControllerStatus.StartPending;
                SstpToggleBtn.Content = active
                    ? "Disable + Neuter Miniports"
                    : "Re-enable Service & Miniports";
            }
            catch
            {
                SstpToggleBtn.Content = "Toggle SSTP";
            }
        }

        // ── SSTP ──
        private Task LoadSstpStatus()
        {
            return Task.Run(() =>
            {
                try
                {
                    using (var sc = new ServiceController("SstpSvc"))
                    {
                        string status = sc.Status.ToString();
                        string startMode = ServiceHelper.GetStartMode("SstpSvc");
                        Dispatcher.Invoke(() => SstpStatusText.Text = $"{status} | Startup: {startMode}");
                    }
                }
                catch
                {
                    Dispatcher.Invoke(() => SstpStatusText.Text = "Service not found");
                }
            });
        }

        private async void SstpToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            SstpToggleBtn.IsEnabled = false;
            try
            {
                bool isRunning;
                using (var sc = new ServiceController("SstpSvc"))
                    isRunning = sc.Status == ServiceControllerStatus.Running
                             || sc.Status == ServiceControllerStatus.StartPending;

                if (isRunning)
                {
                    // ── DISABLE path ──
                    if (MessageBox.Show(
                        "This will stop the SSTP service and disable all VPN miniport adapters (SSTP, IKEv2, L2TP, PPTP, AgileVPN, NDISWAN).\n\nYou can re-enable them at any time with the same button.",
                        "Disable SSTP & Miniports", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                        return;

                    await Task.Run(() =>
                    {
                        ServiceHelper.StopAndDisable("SstpSvc");
                        WanMiniportRemover.SetSstpMiniportsEnabled(false);
                    });

                    SstpToggleBtn.Content = "Re-enable Service & Miniports";
                }
                else
                {
                    // ── ENABLE path ──
                    await Task.Run(() =>
                    {
                        WanMiniportRemover.SetSstpMiniportsEnabled(true);
                        ServiceHelper.EnableAndStart("SstpSvc");
                    });

                    SstpToggleBtn.Content = "Disable + Neuter Miniports";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "SSTP Toggle", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SstpToggleBtn.IsEnabled = true;
                await Task.WhenAll(LoadSstpStatus(), LoadWanMiniports());
            }
        }

        // ── WiFi Direct ──
        private Task LoadWifiDirectStatus()
        {
            return Task.Run(() =>
            {
                string output = ProcessRunner.Run("netsh", "wlan show hostednetwork");
                string status = "Unknown";
                if (output.Contains("Not available")) status = "Not Available";
                else if (output.Contains("Not started")) status = "Not Started";
                else if (output.Contains("Started")) status = "Started (ACTIVE!)";
                Dispatcher.Invoke(() => WifiDirectStatusText.Text = status);
            });
        }

        private async void WifiDirectToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            string output = await ProcessRunner.RunAsync("netsh", "wlan show hostednetwork");
            if (output.Contains("Started"))
            {
                await ProcessRunner.RunAsync("netsh", "wlan stop hostednetwork");
                await ProcessRunner.RunAsync("netsh", "wlan set hostednetwork mode=disallow");
            }
            else
            {
                await ProcessRunner.RunAsync("netsh", "wlan set hostednetwork mode=allow");
            }
            await LoadWifiDirectStatus();
            await LoadWifiDirectAdapters();
        }

        private async void UninstallWifiBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Remove WiFi Direct Virtual Adapter?",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            var results = await Task.Run(() => WanMiniportRemover.RemoveWifiDirect());
            RemovalResultText.Text = string.Join("\n", results);
            await LoadWifiDirectAdapters();
        }

        private Task LoadWifiDirectAdapters()
        {
            return Task.Run(() =>
            {
                var adapters = new List<NetworkAdapterItem>();
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "SELECT Name, NetConnectionStatus FROM Win32_NetworkAdapter WHERE Name LIKE '%Wi-Fi Direct%' OR Name LIKE '%Microsoft Wi-Fi Direct Virtual Adapter%'");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        adapters.Add(new NetworkAdapterItem
                        {
                            Name = obj["Name"]?.ToString() ?? "",
                            Status = DecodeNetStatus(obj["NetConnectionStatus"])
                        });
                    }
                }
                catch { }
                Dispatcher.Invoke(() => WifiDirectGrid.ItemsSource = adapters);
            });
        }

        // ── KDNET ──
        private Task LoadKdnetStatus()
        {
            return Task.Run(() =>
            {
                string output = ProcessRunner.Run("bcdedit", "/enum {current}");
                bool debugOn = output.Contains("debug") && output.Contains("Yes");
                Dispatcher.Invoke(() => KdnetStatusText.Text = debugOn ? "Kernel Debug ENABLED" : "Kernel Debug Disabled");
            });
        }

        private async void KdnetToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            string output = ProcessRunner.Run("bcdedit", "/enum {current}");
            bool debugOn = output.Contains("debug") && output.Contains("Yes");

            if (debugOn)
            {
                await ProcessRunner.RunAsync("bcdedit", "/debug off");
            }
            else
            {
                if (MessageBox.Show("Enable kernel debugging? This is a security risk.",
                    "Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                await ProcessRunner.RunAsync("bcdedit", "/debug on");
            }
            await LoadKdnetStatus();
        }

        private Task LoadKdnetAdapters()
        {
            return Task.Run(() =>
            {
                var adapters = new List<NetworkAdapterItem>();
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "SELECT Name, NetConnectionStatus FROM Win32_NetworkAdapter WHERE Name LIKE '%Kernel Debug%' OR Name LIKE '%KDNIC%'");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        adapters.Add(new NetworkAdapterItem
                        {
                            Name = obj["Name"]?.ToString() ?? "",
                            Status = DecodeNetStatus(obj["NetConnectionStatus"])
                        });
                    }
                }
                catch { }
                Dispatcher.Invoke(() => KdnetGrid.ItemsSource = adapters);
            });
        }

        // ── WAN Miniports ──
        private Task LoadWanMiniports()
        {
            return Task.Run(() =>
            {
                var adapters = new List<NetworkAdapterItem>();
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "SELECT Name, NetConnectionStatus FROM Win32_NetworkAdapter WHERE Name LIKE '%WAN Miniport%'");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        adapters.Add(new NetworkAdapterItem
                        {
                            Name = obj["Name"]?.ToString() ?? "",
                            Status = DecodeNetStatus(obj["NetConnectionStatus"])
                        });
                    }
                }
                catch { }
                Dispatcher.Invoke(() => WanMiniportGrid.ItemsSource = adapters);
            });
        }

        private async void RemoveAllMiniportsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Remove ALL WAN Miniport adapters? This will disable VPN capabilities.",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            var results = await Task.Run(() => WanMiniportRemover.Execute());
            RemovalResultText.Text = results.Count > 0 ? string.Join("\n", results) : "No WAN Miniports found.";
            await LoadWanMiniports();
        }

        public string GetAuditContext()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Command Panel — Network Attack Surface Status:");
            sb.AppendLine($"- SSTP VPN Service: {SstpStatusText?.Text ?? "Unknown"}");
            sb.AppendLine($"- WiFi Direct: {WifiDirectStatusText?.Text ?? "Unknown"}");
            sb.AppendLine($"- Kernel Debug (KDNET): {KdnetStatusText?.Text ?? "Unknown"}");
            var miniports = WanMiniportGrid?.ItemsSource as System.Collections.IEnumerable;
            if (miniports != null)
            {
                var list = new System.Collections.Generic.List<AutoCommand.Models.NetworkAdapterItem>();
                foreach (var item in miniports)
                    if (item is AutoCommand.Models.NetworkAdapterItem na) list.Add(na);
                sb.AppendLine($"- WAN Miniports present: {list.Count}");
                foreach (var na in list)
                    sb.AppendLine($"    • {na.Name} [{na.Status}]");
            }
            return sb.ToString();
        }

        // ── AI Command Assistant ──

        private async void AiPromptInput_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                await ProcessAiRequest();
            }
        }

        private async void AiSendBtn_Click(object sender, RoutedEventArgs e)
        {
            await ProcessAiRequest();
        }

        private async Task ProcessAiRequest()
        {
            string prompt = AiPromptInput.Text.Trim();
            if (string.IsNullOrEmpty(prompt)) return;

            AiPromptInput.Text = "";
            AiPromptInput.IsEnabled = false;
            AiSendBtn.IsEnabled = false;
            AiReviewPanel.Visibility = Visibility.Collapsed;

            AppendChat($"You: {prompt}");
            AppendChat("Gemini: Thinking...");

            try
            {
                bool useLocal = false;
                string targetModel = "gemini-2.5-flash"; // Default
                
                try 
                {
                    string prefsPath = "gemini_model_prefs.txt";
                    if (System.IO.File.Exists(prefsPath))
                    {
                        var parts = System.IO.File.ReadAllText(prefsPath).Split('|');
                        if (parts.Length >= 2)
                        {
                            useLocal = bool.Parse(parts[0]);
                            targetModel = parts[1];
                        }
                    }
                }
                catch { } // Fallback to defaults

                var assistant = new GeminiAssistantService();
                string generatedCommand = await assistant.GenerateCommandAsync(prompt, useLocal, targetModel);

                // Remove the "Thinking..." line
                string currentLog = AiChatLog.Text;
                int lastGeminiIndex = currentLog.LastIndexOf("Gemini: Thinking...");
                if (lastGeminiIndex >= 0)
                {
                    AiChatLog.Text = currentLog.Substring(0, lastGeminiIndex).TrimEnd() + "\n";
                }

                AppendChat("Gemini: Here is the command you requested. Please review it carefully before running.");
                AiSuggestedCommand.Text = generatedCommand;
                AiReviewPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AppendChat($"Error: {ex.Message}");
            }
            finally
            {
                AiPromptInput.IsEnabled = true;
                AiSendBtn.IsEnabled = true;
                AiPromptInput.Focus();
            }
        }

        private void AppendChat(string text)
        {
            AiChatLog.AppendText(text + "\n\n");
            AiChatLog.ScrollToEnd();
        }

        private void AiCancelCommandBtn_Click(object sender, RoutedEventArgs e)
        {
            AiReviewPanel.Visibility = Visibility.Collapsed;
            AiSuggestedCommand.Text = "";
            AppendChat("System: Command execution cancelled by user.");
        }

        private async void AiRunCommandBtn_Click(object sender, RoutedEventArgs e)
        {
            string cmd = AiSuggestedCommand.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) return;

            AiReviewPanel.Visibility = Visibility.Collapsed;
            AiSuggestedCommand.Text = "";
            AppendChat($"System: Executing command...\n> {cmd}");

            try
            {
                // Run via PowerShell
                string result = await ProcessRunner.RunAsync("powershell", $"-NoProfile -Command \"{cmd.Replace("\"", "\\\"")}\"");
                AppendChat(string.IsNullOrWhiteSpace(result) ? "System: Command completed successfully (no output)." : $"Output:\n{result}");
            }
            catch (Exception ex)
            {
                AppendChat($"System Error: {ex.Message}");
            }
        }

        private static string DecodeNetStatus(object status)
        {
            if (status == null) return "N/A";
            return ((int)(ushort)status) switch
            {
                0 => "Disconnected",
                1 => "Connecting",
                2 => "Connected",
                3 => "Disconnecting",
                4 => "Hardware not present",
                5 => "Hardware disabled",
                6 => "Hardware malfunction",
                7 => "Media disconnected",
                _ => status.ToString()
            };
        }
    }
}


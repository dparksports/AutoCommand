using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Threading;
using System.IO;
using AutoCommand.Models;
using AutoCommand.Services;
using AutoCommand.Views;
using AutoCommand.Helpers;

namespace AutoCommand
{
    public partial class MainWindow : Window
    {
        private SecurityEnforcer _enforcer;
        private TrayNotifier _trayNotifier;

        private static string AppVersion =>
            FileVersionInfo.GetVersionInfo(Environment.ProcessPath).ProductVersion ?? "3.7.0";

        public MainWindow()
        {
            InitializeComponent();

            // Live version from the assembly instead of a hardcoded string
            VersionText.Text = $"AutoCommand v{AppVersion}";

            // Set up global exception tracking
            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;

            // Initialize Telemetry
            _ = TelemetryService.Instance.InitializeAsync(HiddenTelemetryWebView);

            InitializeEnforcer();
            LoadAiPrefs();

            // Fire telemetry app_open event (fire-and-forget)
            _ = TelemetryService.Instance.LogEventAsync("app_open", new Dictionary<string, object>
            {
                { "app_version", AppVersion },
                { "os_version", Environment.OSVersion.VersionString }
            });
        }

        private void Current_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _ = TelemetryService.Instance.LogEventAsync("app_exception", new Dictionary<string, object>
            {
                { "message", e.Exception.Message },
                { "stack_trace", e.Exception.StackTrace ?? "No stack trace" }
            });
        }

        private void InitializeEnforcer()
        {
            // Initialize system tray notifier first so it's ready for callbacks
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
            _trayNotifier = new TrayNotifier(iconPath);
            _trayNotifier.ToastActivated += BringWindowToForeground;

            _enforcer = new SecurityEnforcer(OnThreatDetected);
            _enforcer.StatusChanged += OnEnforcerStatusChanged;
            _enforcer.ConfigurationDriftDetected += OnDriftDetected;

            // Wire the tray toast notification for adapter events
            _enforcer.OnAdapterAlert = (title, message) =>
                Dispatcher.Invoke(() => _trayNotifier.ShowSecurityAlert(title, message));

            _enforcer.Start();
        }

        private void BringWindowToForeground()
        {
            Dispatcher.Invoke(() =>
            {
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
                Focus();
            });
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
            else if (_currentThreatType == "Network Adapter")
            {
                string msg = $"Active Adapter Detected: {_currentThreatDetails}\n\n" +
                             "This adapter might be used for unauthorized tunneling or kernel debugging.\n\n" +
                             "Click 'Yes' to Block & Delete (removes device and service).\n" +
                             "Click 'No' to Whitelist (ignore this adapter permanently).\n" +
                             "Click 'Cancel' to ignore for now.";
                             
                var result = MessageBox.Show(msg, "Review Network Adapter", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    _enforcer.MitigateAdapter(_currentThreatDetails);
                    ThreatAlertText.Text = "✓ Adapter mitigated.";
                    ThreatAlertText.Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F));
                    ReviewAlertBtn.Visibility = Visibility.Collapsed;
                }
                else if (result == MessageBoxResult.No)
                {
                    if (_currentThreatDetails.Contains("SSTP")) SecurityEnforcer.IsSstpAllowed = true;
                    if (_currentThreatDetails.Contains("Kernel Debug")) SecurityEnforcer.IsKernelDebugAllowed = true;
                    ThreatAlertText.Text = "✓ Adapter whitelisted.";
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
            _trayNotifier?.Dispose();
            base.OnClosed(e);
        }

        // ── AI state ──
        private bool? _useLocalLlm = null;
        private string _targetModel = "";

        // ── Proactive AI ──
        private DispatcherTimer _debounceTimer;
        private CancellationTokenSource _auditCts;
        private readonly Dictionary<int, AiAuditResult> _tabResultCache = new();
        private AiAuditResult _currentInsightResult;
        private int _pendingTabIdx = -1;

        private void LoadAiPrefs()
        {
            try
            {
                if (File.Exists("gemini_model_prefs.txt"))
                {
                    var lines = File.ReadAllLines("gemini_model_prefs.txt");
                    if (lines.Length >= 2)
                    {
                        bool newUseLocal = lines[0] == "Local";
                        string newModel = lines[1].Trim();

                        // Backwards compatibility check
                        if (newUseLocal && newModel == "llama-server")
                        {
                            newUseLocal = false; // Fallback to avoid null bool errors, or leave null. Let's just set model to empty.
                            newModel = "";
                        }

                        if (_useLocalLlm != newUseLocal || _targetModel != newModel)
                        {
                            _useLocalLlm = newUseLocal;
                            _targetModel = newModel;
                            
                            // Settings changed, invalidate cache
                            _tabResultCache.Clear();
                        }
                    }
                }
            }
            catch { }
        }

        private void AiSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowAiSetup();
        }

        private void AiAuditBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadAiPrefs();
            
            if (_useLocalLlm == null)
            {
                ShowAiSetup();
                return;
            }

            // Clear cache for this tab so manual button always runs fresh
            _tabResultCache.Remove(MainTabControl.SelectedIndex);
            _auditCts?.Cancel();
            _debounceTimer?.Stop();
            AiInsightPanel.Visibility = System.Windows.Visibility.Collapsed;

            string contextData;
            if (MainTabControl.SelectedContent is IAiAuditable auditable)
            {
                contextData = auditable.GetAuditContext();
            }
            else
            {
                string tabName = "Unknown Tab";
                if (MainTabControl.SelectedItem is TabItem ti && ti.Header is StackPanel sp
                    && sp.Children.Count > 1 && sp.Children[1] is System.Windows.Controls.TextBlock tb)
                    tabName = tb.Text;
                contextData = $"The user is looking at the '{tabName}' tab, but this tab currently has no data hooked up for auditing. Please inform the user that this tab cannot be audited right now.";
            }

            var dialog = new AiAuditDialog(contextData, _useLocalLlm.Value, _targetModel);
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void ShowAiSetup()
        {
            MainTabControl.SelectedItem = AiSetupTabItem;
        }

        // ── Proactive AI: tab-switch handler ──────────────────────────────────

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: nested selection events from DataGrids etc. inside tabs fire this too
            if (!ReferenceEquals(e.OriginalSource, MainTabControl)) return;

            string tabName = "Unknown";
            if (MainTabControl.SelectedItem is TabItem ti && ti.Header is StackPanel sp
                && sp.Children.Count > 1 && sp.Children[1] is System.Windows.Controls.TextBlock tb)
            {
                tabName = tb.Text;
            }

            _ = TelemetryService.Instance.LogEventAsync("tab_view", new Dictionary<string, object>
            {
                { "tab_name", tabName }
            });

            LoadAiPrefs();

            _debounceTimer?.Stop();
            _auditCts?.Cancel();
            _currentInsightResult = null;
            AiInsightPanel.Visibility = System.Windows.Visibility.Collapsed;

            if (_useLocalLlm == null) return;
            if (MainTabControl.SelectedContent is not IAiAuditable) return;

            int tabIdx = MainTabControl.SelectedIndex;

            // Show cached result immediately if available
            if (_tabResultCache.TryGetValue(tabIdx, out var cached))
            {
                _currentInsightResult = cached;
                ShowInsightPanel(cached);
                return;
            }

            _pendingTabIdx = tabIdx;
            ShowInsightLoading();

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _debounceTimer.Tick += OnDebounceElapsed;
            _debounceTimer.Start();
        }

        private async void OnDebounceElapsed(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            int tabIdx = _pendingTabIdx;

            if (MainTabControl.SelectedIndex != tabIdx) return;
            if (MainTabControl.SelectedContent is not IAiAuditable auditable) return;

            // For local LLM — check if the model is loaded into memory
            if (_useLocalLlm == true && !LocalLlmManager.IsModelLoaded(_targetModel))
            {
                ShowInsightModelNotLoaded();
                return;
            }

            await RunInsightAnalysisAsync(tabIdx, auditable);
        }

        private async Task RunInsightAnalysisAsync(int tabIdx, IAiAuditable auditable)
        {
            _auditCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var ct = _auditCts.Token;

            try
            {
                string contextData = auditable.GetAuditContext();
                var svc = new GeminiAssistantService();
                var result = await svc.AnalyzeContextAsync(contextData, _useLocalLlm.Value, _targetModel, ct);

                if (ct.IsCancellationRequested || MainTabControl.SelectedIndex != tabIdx) return;

                _tabResultCache[tabIdx] = result;
                _currentInsightResult = result;
                ShowInsightPanel(result);
            }
            catch (OperationCanceledException) { /* Tab changed or timed out */ }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    ShowInsightError(ex.Message);
            }
        }

        // ── Insight panel helpers ─────────────────────────────────────────────

        private void ShowInsightLoading()
        {
            AiInsightDot.Fill = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
            AiInsightText.Text = "Analyzing security data…";
            AiInsightStartBtn.Visibility = System.Windows.Visibility.Collapsed;
            AiInsightDetailBtn.Visibility = System.Windows.Visibility.Collapsed;
            AiInsightPanel.Visibility = System.Windows.Visibility.Visible;
        }

        private void ShowInsightPanel(AiAuditResult result)
        {
            string level = result.ThreatLevel?.ToLower() ?? "unknown";
            Color dot;
            string prefix;

            if (level.Contains("safe"))           { dot = Color.FromRgb(0x4A, 0xDE, 0x80); prefix = "✅"; }
            else if (level.Contains("danger") ||
                     level.Contains("critical"))  { dot = Color.FromRgb(0xF8, 0x71, 0x71); prefix = "🔴"; }
            else if (level.Contains("caution") ||
                     level.Contains("warning"))   { dot = Color.FromRgb(0xFA, 0xCC, 0x15); prefix = "⚠️"; }
            else                                  { dot = Color.FromRgb(0x94, 0xA3, 0xB8); prefix = "ℹ️"; }

            AiInsightDot.Fill = new SolidColorBrush(dot);
            AiInsightText.Text = $"{prefix}  {result.Summary}";
            AiInsightStartBtn.Visibility = System.Windows.Visibility.Collapsed;
            AiInsightDetailBtn.Visibility = System.Windows.Visibility.Visible;
            AiInsightPanel.Visibility = System.Windows.Visibility.Visible;
        }

        private void ShowInsightError(string message)
        {
            AiInsightDot.Fill = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
            AiInsightText.Text = $"AI unavailable: {message.Split('\n')[0]}";
            AiInsightStartBtn.Visibility = System.Windows.Visibility.Collapsed;
            AiInsightDetailBtn.Visibility = System.Windows.Visibility.Collapsed;
            AiInsightPanel.Visibility = System.Windows.Visibility.Visible;
        }

        private void ShowInsightModelNotLoaded()
        {
            AiInsightDot.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            AiInsightDetailBtn.Visibility = System.Windows.Visibility.Collapsed;

            if (string.IsNullOrEmpty(_targetModel))
            {
                AiInsightText.Text = "🧠  No local AI model configured. Please click settings to select a model.";
                AiInsightStartBtn.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                AiInsightText.Text = $"🧠  Model not loaded in memory ({System.IO.Path.GetFileName(_targetModel)})";
                AiInsightStartBtn.Content = "⬇ Load Model";
                AiInsightStartBtn.IsEnabled = true;
                AiInsightStartBtn.Visibility = System.Windows.Visibility.Visible;
            }

            AiInsightPanel.Visibility = System.Windows.Visibility.Visible;
        }

        private async void AiInsightStartBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_targetModel)) return;

            AiInsightStartBtn.IsEnabled = false;
            AiInsightStartBtn.Content = "⏳";

            int tabIdx = MainTabControl.SelectedIndex;
            var progress = new Progress<string>(msg =>
            {
                AiInsightText.Text = $"🧠  {msg}";
            });

            _auditCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // Timeout doesn't strictly apply to LLama weights loading, but good for tracking

            try
            {
                await LocalLlmManager.LoadModelAsync(_targetModel, progress);

                // Guard: user may have switched tabs while we were waiting
                if (MainTabControl.SelectedIndex != tabIdx) return;

                AiInsightStartBtn.Visibility = System.Windows.Visibility.Collapsed;
                ShowInsightLoading();
                AiInsightText.Text = "✅  Model loaded — analyzing…";

                if (MainTabControl.SelectedContent is IAiAuditable auditable)
                    await RunInsightAnalysisAsync(tabIdx, auditable);
            }
            catch (Exception ex)
            {
                AiInsightText.Text = $"❌  Could not load model: {ex.Message.Split('\n')[0]}";
                AiInsightStartBtn.Content = "⬇ Retry";
                AiInsightStartBtn.IsEnabled = true;
            }
        }

        private void AiInsightDetailBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentInsightResult == null) return;
            var dialog = new AiAuditDialog(_currentInsightResult);
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void AiInsightDismissBtn_Click(object sender, RoutedEventArgs e)
        {
            AiInsightPanel.Visibility = System.Windows.Visibility.Collapsed;
        }
    }
}


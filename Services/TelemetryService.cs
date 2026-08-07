using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AutoCommand.Services
{
    public class TelemetryService
    {
        private static readonly Lazy<TelemetryService> _instance = new(() => new TelemetryService());
        public static TelemetryService Instance => _instance.Value;

        private WebView2 _webView;
        private bool _isReady;
        private bool _isFailed;
        private readonly ConcurrentQueue<Func<Task>> _pendingOperations = new();

        // Privacy control: Set to false to disable telemetry globally
        public bool ConsentGranted { get; set; } = true;

        private TelemetryService() { }

        /// <summary>
        /// Initializes the WebView2 control and loads the Firebase HTML.
        /// </summary>
        public async Task InitializeAsync(WebView2 webView)
        {
            if (webView == null) throw new ArgumentNullException(nameof(webView));
            _webView = webView;

            try
            {
                // Ensure environment is set up (creates user data folder if needed)
                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "AutoCommand_WebView2"));
                await _webView.EnsureCoreWebView2Async(env);

                // Listen for messages from our HTML script
                _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                // Map local Web assets folder to a virtual host to avoid file:// origin CORS restrictions in Firebase Analytics
                string webDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Web");
                string htmlPath = Path.Combine(webDir, "telemetry.html");

                if (File.Exists(htmlPath))
                {
                    _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "telemetry.local",
                        webDir,
                        CoreWebView2HostResourceAccessKind.Allow
                    );
                    _webView.CoreWebView2.Navigate("https://telemetry.local/telemetry.html");
                }
                else
                {
                    Debug.WriteLine($"[Telemetry] HTML file not found at: {htmlPath}");
                    _isFailed = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] Failed to initialize WebView2: {ex.Message}");
                _isFailed = true; // Silently disable telemetry
            }
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = e.WebMessageAsJson;
                if (string.IsNullOrEmpty(msg)) return;

                using var doc = JsonDocument.Parse(msg);
                if (doc.RootElement.TryGetProperty("type", out var typeElement))
                {
                    string type = typeElement.GetString();
                    if (type == "ready")
                    {
                        Debug.WriteLine("[Telemetry] Firebase SDK is ready.");
                        _isReady = true;
                        FlushPendingOperations();
                    }
                    else if (type == "error")
                    {
                        string errMsg = doc.RootElement.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : "Unknown";
                        Debug.WriteLine($"[Telemetry] Firebase JS Error: {errMsg}");
                        _isFailed = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] Error parsing message from WebView2: {ex.Message}");
            }
        }

        private void FlushPendingOperations()
        {
            if (!_isReady || _isFailed) return;

            while (_pendingOperations.TryDequeue(out var op))
            {
                // Execute on the UI thread since WebView2 requires it
                _webView.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await op();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Telemetry] Error executing pending op: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// Logs a telemetry event. It is fire-and-forget.
        /// </summary>
        public async Task LogEventAsync(string eventName, Dictionary<string, object> parameters = null)
        {
            if (!ConsentGranted || _isFailed) return;

            // Ensure parameters dictionary exists
            parameters ??= new Dictionary<string, object>();

#if DEBUG
            // Enable Firebase DebugView
            parameters["debug_mode"] = true;
#endif

            string jsonParams = JsonSerializer.Serialize(parameters);
            string script = $"window.logTelemetryEvent('{eventName}', {jsonParams});";

            await ExecuteScriptAsync(script);
        }

        public async Task SetUserIdAsync(string userId)
        {
            if (!ConsentGranted || _isFailed) return;
            string script = $"window.setTelemetryUserId('{userId}');";
            await ExecuteScriptAsync(script);
        }

        public async Task SetUserPropertiesAsync(Dictionary<string, object> properties)
        {
            if (!ConsentGranted || _isFailed) return;
            string jsonProps = properties != null ? JsonSerializer.Serialize(properties) : "{}";
            string script = $"window.setTelemetryUserProperties({jsonProps});";
            await ExecuteScriptAsync(script);
        }

        private Task ExecuteScriptAsync(string script)
        {
            if (_isReady)
            {
                // Must be on UI thread
                _webView.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await _webView.CoreWebView2.ExecuteScriptAsync(script);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Telemetry] Script execution failed: {ex.Message}");
                    }
                });
                return Task.CompletedTask;
            }
            else
            {
                // Enqueue if not ready
                _pendingOperations.Enqueue(async () =>
                {
                    await _webView.CoreWebView2.ExecuteScriptAsync(script);
                });
                return Task.CompletedTask;
            }
        }
    }
}

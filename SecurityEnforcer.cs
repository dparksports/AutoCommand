using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace AutoCommand
{
    /// <summary>
    /// Background security enforcement loop.
    /// Ported from DeviceMonitorCS — all PowerShell replaced with native APIs.
    /// Adapter detection is event-driven via AdapterEventWatcher (zero polling overhead),
    /// backed by a state-reconciliation sweep that re-asserts the desired SSTP service
    /// and kernel-debug state so anything re-enabled silently is neutralized again.
    /// </summary>
    public class SecurityEnforcer
    {
        private bool _isRunning;
        private readonly Action<string, string> _onThreatDetected;
        public event Action<string, string> StatusChanged;

        /// <summary>Optional extra callback to trigger system-level notifications (e.g. tray balloon).</summary>
        public Action<string, string> OnAdapterAlert;

        private Helpers.AdapterEventWatcher _adapterWatcher;

        public SecurityEnforcer(Action<string, string> onThreatDetected)
        {
            _onThreatDetected = onThreatDetected;
        }

        private static volatile bool _isSstpAllowed = false;
        public static bool IsSstpAllowed
        {
            get => _isSstpAllowed;
            set
            {
                _isSstpAllowed = value;
                try { System.IO.File.WriteAllText("allowed_sstp.txt", value.ToString()); } catch { }
            }
        }

        private static volatile bool _isKernelDebugAllowed = false;
        public static bool IsKernelDebugAllowed
        {
            get => _isKernelDebugAllowed;
            set
            {
                _isKernelDebugAllowed = value;
                try { System.IO.File.WriteAllText("allowed_kerneldebug.txt", value.ToString()); } catch { }
            }
        }

        // AutoMitigateAdapters: cached in-memory; persisted to disk via setter only.
        private static volatile bool _autoMitigateAdapters = false;
        public static bool AutoMitigateAdapters
        {
            get => _autoMitigateAdapters;
            set
            {
                _autoMitigateAdapters = value;
                try { System.IO.File.WriteAllText("auto_mitigate_adapters.txt", value.ToString()); } catch { }
            }
        }

        // Load all persisted preferences from disk — called once at Start().
        private static void LoadPersistedPreferences()
        {
            try
            {
                if (System.IO.File.Exists("allowed_sstp.txt"))
                    _isSstpAllowed = System.IO.File.ReadAllText("allowed_sstp.txt").Trim() == "True";
                if (System.IO.File.Exists("allowed_kerneldebug.txt"))
                    _isKernelDebugAllowed = System.IO.File.ReadAllText("allowed_kerneldebug.txt").Trim() == "True";
                if (System.IO.File.Exists("auto_mitigate_adapters.txt"))
                    _autoMitigateAdapters = System.IO.File.ReadAllText("auto_mitigate_adapters.txt").Trim() == "True";
            }
            catch { }
        }

        private static HashSet<string> _whitelistedTasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void WhitelistTask(string taskName)
        {
            lock (_whitelistedTasks)
            {
                _whitelistedTasks.Add(taskName);
            }
        }

        public void Start()
        {
            if (_isRunning) return;
            LoadPersistedPreferences();
            _isRunning = true;

            // Start the event-driven adapter watcher (replaces 2s WMI polling)
            _adapterWatcher = new Helpers.AdapterEventWatcher();
            _adapterWatcher.AdapterAppeared += OnAdapterAppeared;
            _adapterWatcher.Start();

            Task.Run(RunLoop);
        }

        public void Stop()
        {
            _isRunning = false;
            _adapterWatcher?.Dispose();
            _adapterWatcher = null;
        }

        public int CheckInterval { get; set; } = 5000;
        private int _loopCount = 0;

        // Interval multipliers (based on CheckInterval = 5000ms)
        // 5s  * 6  = 30s for hosts file
        // 5s  * 12 = 60s for privileged task scan
        // 5s  * 12 = 60s for firewall drift (was every 30s at 2s interval, now same cadence)
        // 5s  * 6  = 30s for kernel debug state (spawns bcdedit)
        private const int HostsFileEveryN       = 6;   // every ~30s
        private const int PrivTasksEveryN        = 12;  // every ~60s
        private const int FirewallDriftEveryN    = 12;  // every ~60s
        private const int KdnetEnforceEveryN     = 6;   // every ~30s

        // Debounce so drift alerts don't spam every tick while the user decides
        private static readonly TimeSpan DriftAlertDebounce = TimeSpan.FromSeconds(60);
        private DateTime _lastSstpDriftAlertUtc = DateTime.MinValue;
        private DateTime _lastKdnetDriftAlertUtc = DateTime.MinValue;

        public event Action<List<string>> ConfigurationDriftDetected;

        private async Task RunLoop()
        {
            while (_isRunning)
            {
                try
                {
                    // Hosted network is lightweight — run every tick
                    CheckHostedNetwork();

                    // SSTP service & miniport state — reconcile every tick (cheap SCM/registry reads)
                    EnforceSstpService();

                    // Kernel debug (bcdedit + KDNIC device) — every 30s (spawns bcdedit)
                    if (_loopCount % KdnetEnforceEveryN == 0)
                        EnforceKernelDebug();

                    // Hosts file — run every 30s
                    if (_loopCount % HostsFileEveryN == 0)
                        CheckHostsFile();

                    // Task scheduler scan — heavy, run every 60s
                    if (_loopCount % PrivTasksEveryN == 0)
                        CheckPrivilegedTasks();

                    // Firewall drift — run every 60s
                    if (_loopCount % FirewallDriftEveryN == 0)
                        await MonitorFirewallDrift();

                    _loopCount++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Enforcer Error: {ex.Message}");
                }
                await Task.Delay(CheckInterval);
            }
        }

        public void ForceApplyFirewallRules()
        {
            try
            {
                var overrides = Helpers.FirewallConfigManager.Instance.RuleOverrides;
                if (overrides.Count == 0) return;

                // Use COM API to apply overrides
                Type fwPolicyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                dynamic fwPolicy = Activator.CreateInstance(fwPolicyType);
                foreach (dynamic rule in fwPolicy.Rules)
                {
                    try
                    {
                        string name = (string)rule.Name;
                        if (overrides.TryGetValue(name, out string expected))
                        {
                            rule.Enabled = expected == "True";
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Firewall Enforcer Error: {ex.Message}");
            }
        }

        private async Task MonitorFirewallDrift()
        {
            try
            {
                var overrides = Helpers.FirewallConfigManager.Instance.RuleOverrides;
                if (overrides.Count == 0) return;

                var driftItems = await Services.FirewallService.Instance.CheckDriftAsync(overrides);
                if (driftItems.Count > 0)
                {
                    ConfigurationDriftDetected?.Invoke(driftItems);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Drift Check Error: {ex.Message}");
            }
        }

        private void CheckHostedNetwork()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_NetworkAdapter WHERE Name LIKE '%Hosted Network Virtual Adapter%' AND NetConnectionStatus = 2");
                if (searcher.Get().Count > 0)
                {
                    // Kill it using netsh directly (not PowerShell)
                    Helpers.ProcessRunner.RunDetached("netsh", "wlan stop hostednetwork");
                    Helpers.ProcessRunner.RunDetached("netsh", "wlan set hostednetwork mode=disallow");

                    _onThreatDetected?.Invoke("Hosted Network", "Active Hosted Network detected and disabled.");
                    StatusChanged?.Invoke("Threat Blocked: Hosted Network", "Red");
                }
            }
            catch { }
        }

        // ── State reconciliation sweep ────────────────────────────────────────
        // The adapter event watcher only fires when a device is created or flips to
        // working state. Re-enables that bypass device events — SstpSvc simply being
        // started again, or kernel debug flipped back on in the BCD — are caught here
        // by verifying the desired state every tick and neutralizing any drift.

        /// <summary>
        /// Reconciles the SSTP attack surface every tick: unless the user allow-listed
        /// SSTP, SstpSvc must be stopped AND disabled (Start=4). Windows Update and RAS
        /// reconfiguration can re-enable both silently, so the state is re-asserted.
        /// </summary>
        private void EnforceSstpService()
        {
            if (IsSstpAllowed) return;
            try
            {
                bool running;
                try
                {
                    using (var sc = new ServiceController("SstpSvc"))
                    {
                        running = sc.Status == ServiceControllerStatus.Running
                               || sc.Status == ServiceControllerStatus.StartPending;
                    }
                }
                catch
                {
                    return; // Service not installed — nothing to enforce
                }

                int startValue = GetServiceStartValue("SstpSvc");
                if (startValue == -1) return; // registry unreadable — skip this tick
                bool startDisabled = startValue == 4; // SERVICE_DISABLED

                if (!running && startDisabled) return; // desired state

                if (AutoMitigateAdapters)
                {
                    Helpers.ServiceHelper.StopAndDisable("SstpSvc");
                    Helpers.WanMiniportRemover.SetSstpMiniportsEnabled(false);

                    if (DateTime.UtcNow - _lastSstpDriftAlertUtc >= DriftAlertDebounce)
                    {
                        _lastSstpDriftAlertUtc = DateTime.UtcNow;
                        _onThreatDetected?.Invoke("SSTP Service",
                            $"SstpSvc was re-enabled ({(running ? "service running" : "startup type restored")}) and has been stopped and disabled again.");
                        StatusChanged?.Invoke("Threat Blocked: SSTP Service", "Red");
                        OnAdapterAlert?.Invoke("🔴 AutoCommand blocked: SSTP service",
                            "SstpSvc was re-enabled and has been stopped and disabled again.");
                    }
                }
                else if (DateTime.UtcNow - _lastSstpDriftAlertUtc >= DriftAlertDebounce)
                {
                    _lastSstpDriftAlertUtc = DateTime.UtcNow;
                    // "WAN Miniport (SSTP)" keeps the existing Review flow working:
                    // Block & Delete stops/disables the service, No whitelists SSTP.
                    _onThreatDetected?.Invoke("Network Adapter", "WAN Miniport (SSTP)");
                    StatusChanged?.Invoke("Threat Detected: SSTP Service", "Amber");
                    OnAdapterAlert?.Invoke("⚠ Security Alert: SSTP Service Re-enabled",
                        "SstpSvc is no longer disabled. Click to review and block it.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnforceSstpService Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Reconciles the KDNET attack surface every ~30s: unless allow-listed, kernel
        /// debug must be OFF in the BCD and no working KDNIC adapter may be present.
        /// </summary>
        private void EnforceKernelDebug()
        {
            if (IsKernelDebugAllowed) return;
            try
            {
                bool debugOn = false;
                string bcdOutput = Helpers.ProcessRunner.Run("bcdedit", "/enum {current}");
                if (!string.IsNullOrEmpty(bcdOutput))
                {
                    debugOn = bcdOutput.IndexOf("debug", StringComparison.OrdinalIgnoreCase) >= 0
                           && bcdOutput.IndexOf("yes", StringComparison.OrdinalIgnoreCase) >= 0;
                }

                bool kdnicActive = IsKdnicAdapterActive();

                if (!debugOn && !kdnicActive) return; // desired state

                if (AutoMitigateAdapters)
                {
                    if (debugOn)
                        Helpers.ProcessRunner.RunDetached("bcdedit", "/debug off");
                    if (kdnicActive)
                        Helpers.WanMiniportRemover.RemoveKdnet();

                    if (DateTime.UtcNow - _lastKdnetDriftAlertUtc >= DriftAlertDebounce)
                    {
                        _lastKdnetDriftAlertUtc = DateTime.UtcNow;
                        _onThreatDetected?.Invoke("Kernel Debug", debugOn
                            ? "Kernel debugging was re-enabled in the boot configuration and has been turned off again."
                            : "An active Kernel Debug adapter was detected and removed.");
                        StatusChanged?.Invoke("Threat Blocked: Kernel Debug", "Red");
                        OnAdapterAlert?.Invoke("🔴 AutoCommand blocked: Kernel Debug",
                            debugOn
                                ? "Kernel debugging (bcdedit) was re-enabled and turned off again."
                                : "An active Kernel Debug (KDNIC) adapter was detected and removed.");
                    }
                }
                else if (DateTime.UtcNow - _lastKdnetDriftAlertUtc >= DriftAlertDebounce)
                {
                    _lastKdnetDriftAlertUtc = DateTime.UtcNow;
                    _onThreatDetected?.Invoke("Network Adapter", "Kernel Debug Network Adapter");
                    StatusChanged?.Invoke("Threat Detected: Kernel Debug", "Amber");
                    OnAdapterAlert?.Invoke("⚠ Security Alert: Kernel Debug Detected",
                        debugOn
                            ? "Kernel debugging is enabled in the boot configuration. Click to review."
                            : "An active Kernel Debug (KDNIC) adapter is present. Click to review.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnforceKernelDebug Error: {ex.Message}");
            }
        }

        private static bool IsKdnicAdapterActive()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE Name LIKE '%Kernel Debug%' OR Name LIKE '%KDNIC%'"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var raw = obj["ConfigManagerErrorCode"];
                        int err = raw == null ? 0 : Convert.ToInt32(raw);
                        if (err == 0) return true; // present and working
                    }
                }
            }
            catch { }
            return false;
        }

        private static int GetServiceStartValue(string serviceName)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName))
                {
                    if (key == null) return -1; // service key missing — not installed
                    var raw = key.GetValue("Start");
                    return raw == null ? -1 : Convert.ToInt32(raw);
                }
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Called by AdapterEventWatcher when a monitored adapter appears.
        /// This fires once per adapter appearance — no spam, no polling cost.
        /// </summary>
        private void OnAdapterAppeared(string adapterName)
        {
            bool isSstp        = adapterName.IndexOf("SSTP", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isKernelDebug = adapterName.IndexOf("Kernel Debug", StringComparison.OrdinalIgnoreCase) >= 0
                              || adapterName.IndexOf("KDNIC",       StringComparison.OrdinalIgnoreCase) >= 0;

            if (isSstp && IsSstpAllowed) return;
            if (isKernelDebug && IsKernelDebugAllowed) return;

            string threatType    = isSstp ? "WAN Miniport (SSTP)" : "Kernel Debug Adapter";
            string displayName   = isSstp ? "WAN Miniport (SSTP)" : "Kernel Debug Network Adapter";
            string statusColor   = "Amber";

            if (AutoMitigateAdapters)
            {
                MitigateAdapter(displayName);
                _onThreatDetected?.Invoke(threatType, $"Active adapter detected and auto-mitigated: {adapterName}");
                StatusChanged?.Invoke($"Threat Blocked: {threatType}", "Red");
                // Also fire the tray notification for auto-mitigated events
                OnAdapterAlert?.Invoke(
                    $"🔴 AutoCommand blocked: {threatType}",
                    $"The {adapterName} adapter was detected and automatically removed.");
            }
            else
            {
                _onThreatDetected?.Invoke("Network Adapter", displayName);
                StatusChanged?.Invoke($"Threat Detected: {threatType}", statusColor);
                // Fire the tray balloon notification so user sees it even if app is minimized
                OnAdapterAlert?.Invoke(
                    $"⚠ Security Alert: {threatType} Detected",
                    $"The {adapterName} adapter has become active. Click to review and block it.");
            }
        }

        /// <summary>
        /// Mitigates an adapter by its friendly name using SetupAPI (native, no pnputil shell).
        /// For SSTP, also stops and disables the SstpSvc service.
        /// </summary>
        public void MitigateAdapter(string adapterFriendlyName)
        {
            Task.Run(() =>
            {
                try
                {
                    bool isSstp = adapterFriendlyName.IndexOf("SSTP", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isKdnic = adapterFriendlyName.IndexOf("Kernel Debug", StringComparison.OrdinalIgnoreCase) >= 0
                                || adapterFriendlyName.IndexOf("KDNIC", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isSstp)
                    {
                        // Stop the RAS/SSTP service first
                        Helpers.ServiceHelper.StopAndDisable("SstpSvc");
                        // Use native SetupAPI via WanMiniportRemover (more reliable than pnputil)
                        var results = Helpers.WanMiniportRemover.RemoveSstpMiniports();
                        foreach (var r in results)
                            Debug.WriteLine($"MitigateAdapter (SSTP): {r}");
                    }
                    else if (isKdnic)
                    {
                        var results = Helpers.WanMiniportRemover.RemoveKdnet();
                        foreach (var r in results)
                            Debug.WriteLine($"MitigateAdapter (KDNET): {r}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MitigateAdapter Error: {ex.Message}");
                }
            });
        }

        private void CheckPrivilegedTasks()
        {
            try
            {
                // Use TaskScheduler COM to check for non-MS high privilege tasks
                Type tsType = Type.GetTypeFromProgID("Schedule.Service");
                dynamic ts = Activator.CreateInstance(tsType);
                ts.Connect();
                dynamic rootFolder = ts.GetFolder("\\");
                CheckTasksInFolder(rootFolder);
            }
            catch { }
        }

        private void CheckTasksInFolder(dynamic folder)
        {
            try
            {
                dynamic tasks = folder.GetTasks(1);
                foreach (dynamic task in tasks)
                {
                    try
                    {
                        string path = (string)task.Path;
                        // Skip Microsoft tasks to reduce noise
                        if (path.Contains("\\Microsoft\\")) continue;

                        dynamic def = task.Definition;
                        dynamic principal = def.Principal;
                        int runLevel = (int)principal.RunLevel;

                        // RunLevel 1 = TASK_RUNLEVEL_HIGHEST
                        if (runLevel == 1)
                        {
                            string name = task.Name;
                            bool isWhitelisted = false;
                            lock (_whitelistedTasks)
                            {
                                if (_whitelistedTasks.Contains(name)) isWhitelisted = true;
                            }

                            if (!isWhitelisted)
                            {
                                _onThreatDetected?.Invoke("Privileged Task", $"High Risk Task detected: {name}");
                                StatusChanged?.Invoke("Warning: Privileged Task", "Amber");
                            }
                        }
                    }
                    catch { }
                }

                dynamic subFolders = folder.GetFolders(0);
                foreach (dynamic sub in subFolders)
                {
                    try
                    {
                        string subPath = sub.Path;
                        if (subPath.Contains("\\Microsoft\\")) continue;
                        CheckTasksInFolder(sub);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void CheckHostsFile()
        {
            try
            {
                string hostsPath = @"C:\Windows\System32\drivers\etc\hosts";
                if (!System.IO.File.Exists(hostsPath)) return;

                var lines = System.IO.File.ReadAllLines(hostsPath);
                foreach (var line in lines)
                {
                    var t = line.Trim();
                    if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;

                    if (!t.Contains("localhost") && !t.Contains("127.0.0.1") && !t.Contains("::1"))
                    {
                        _onThreatDetected?.Invoke("Hosts File Modification", $"Suspicious redirect found: {t}");
                        StatusChanged?.Invoke("Warning: Hosts Modified", "Amber");
                        return; // Found a bad one, alert and return
                    }
                }
            }
            catch { }
        }
    }
}


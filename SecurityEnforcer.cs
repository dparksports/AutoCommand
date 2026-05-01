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
    /// </summary>
    public class SecurityEnforcer
    {
        private bool _isRunning;
        private readonly Action<string, string> _onThreatDetected;
        public event Action<string, string> StatusChanged;

        public SecurityEnforcer(Action<string, string> onThreatDetected)
        {
            _onThreatDetected = onThreatDetected;
        }

        private static volatile bool _isSstpAllowed = false;
        public static bool IsSstpAllowed
        {
            get => _isSstpAllowed;
            set => _isSstpAllowed = value;
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            Task.Run(RunLoop);
        }

        public void Stop()
        {
            _isRunning = false;
        }

        public int CheckInterval { get; set; } = 2000;
        private int _loopCount = 0;

        public event Action<List<string>> ConfigurationDriftDetected;

        private async Task RunLoop()
        {
            while (_isRunning)
            {
                try
                {
                    CheckHostedNetwork();
                    CheckWanMiniports();
                    CheckPrivilegedTasks();
                    CheckHostsFile();

                    if (_loopCount % 15 == 0) // Every 30 seconds
                    {
                        await MonitorFirewallDrift();
                    }
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

        private void CheckWanMiniports()
        {
            try
            {
                bool exists = false;
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "SELECT * FROM Win32_NetworkAdapter WHERE Name LIKE '%WAN Miniport (SSTP)%'");
                    exists = searcher.Get().Count > 0;
                }
                catch { }

                if (exists)
                {
                    if (IsSstpAllowed)
                    {
                        Debug.WriteLine("SecurityEnforcer: SSTP DETECTED (USER WHITELISTED). TAKEDOWN BYPASSED.");
                        return;
                    }

                    Debug.WriteLine("SecurityEnforcer: WAN Miniport (SSTP) detected. Initiating takedown...");

                    // 1. Stop SstpSvc using ServiceController
                    Helpers.ServiceHelper.StopAndDisable("SstpSvc");

                    // 3. Disable Adapter via WMI
                    try
                    {
                        var searcher = new ManagementObjectSearcher(
                            "SELECT PNPDeviceID FROM Win32_NetworkAdapter WHERE Name LIKE '%WAN Miniport (SSTP)%'");
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            string deviceId = obj["PNPDeviceID"]?.ToString();
                            if (!string.IsNullOrEmpty(deviceId))
                            {
                                // Use pnputil directly (not PowerShell)
                                Helpers.ProcessRunner.RunDetached("pnputil.exe", $"/remove-device \"{deviceId}\"");
                                Debug.WriteLine($"SecurityEnforcer: Removed device {deviceId}");
                                break;
                            }
                        }
                    }
                    catch { }

                    _onThreatDetected?.Invoke("WAN Miniport (SSTP)",
                        "Active SSTP Adapter detected. Service stopped, disabled, and device uninstalled.");
                    StatusChanged?.Invoke("Threat Blocked: SSTP", "Red");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CheckWanMiniports Error: {ex.Message}");
            }
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
                            _onThreatDetected?.Invoke("Privileged Task", $"High Risk Task detected: {name}");
                            StatusChanged?.Invoke("Warning: Privileged Task", "Amber");
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


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AutoCommand.Models;

namespace AutoCommand.Services
{
    /// <summary>
    /// Native Windows Firewall management via INetFwPolicy2 COM.
    /// Replaces all PowerShell Get-NetFirewallRule / Set-NetFirewallRule calls.
    /// Uses dynamic COM — no COM reference DLL needed.
    /// </summary>
    public class FirewallService
    {
        private static FirewallService _instance;
        public static FirewallService Instance => _instance ??= new FirewallService();

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        private static readonly Dictionary<string, string> GroupNameCache = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// COM's INetFwRule.Grouping returns indirect resource strings (e.g. "@FirewallAPI.dll,-32752"),
        /// not the localized display names ("Network Discovery") that the profile presets use.
        /// </summary>
        public static string ResolveGroupName(string grouping)
        {
            if (string.IsNullOrEmpty(grouping) || grouping[0] != '@') return grouping;
            lock (GroupNameCache)
            {
                if (GroupNameCache.TryGetValue(grouping, out string cached)) return cached;
                string resolved = grouping;
                var sb = new StringBuilder(512);
                if (SHLoadIndirectString(grouping, sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0)
                    resolved = sb.ToString();
                GroupNameCache[grouping] = resolved;
                return resolved;
            }
        }

        /// <summary>
        /// Case-insensitive group match against both the raw Grouping string and its resolved display name.
        /// </summary>
        private static bool GroupMatches(string ruleGrouping, string targetName)
        {
            if (string.IsNullOrEmpty(ruleGrouping) || string.IsNullOrEmpty(targetName)) return false;
            if (string.Equals(ruleGrouping, targetName, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(ResolveGroupName(ruleGrouping), targetName, StringComparison.OrdinalIgnoreCase);
        }

        private dynamic GetPolicy()
        {
            Type fwPolicyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (fwPolicyType == null)
                throw new InvalidOperationException("Windows Firewall COM component not available.");
            return Activator.CreateInstance(fwPolicyType);
        }

        /// <summary>
        /// Load all firewall rules for a given direction using COM API.
        /// direction: 1 = Inbound, 2 = Outbound
        /// </summary>
        public Task<List<FirewallRuleItem>> LoadRulesAsync(int direction)
        {
            return Task.Run(() =>
            {
                var results = new List<FirewallRuleItem>();
                try
                {
                    dynamic fwPolicy = GetPolicy();
                    foreach (dynamic rule in fwPolicy.Rules)
                    {
                        try
                        {
                            int ruleDir = (int)rule.Direction;
                            if (ruleDir != direction) continue;

                            string grouping = ResolveGroupName((string)rule.Grouping) ?? "";
                            results.Add(new FirewallRuleItem
                            {
                                Name = rule.Name ?? "",
                                DisplayName = rule.Name ?? "",
                                DisplayGroup = string.IsNullOrEmpty(grouping) ? "(Ungrouped)" : grouping,
                                Direction = direction == 1 ? "Inbound" : "Outbound",
                                Enabled = (bool)rule.Enabled,
                                Action = ((int)rule.Action) == 1 ? "Allow" : "Block",
                                Profile = DecodeProfile((int)rule.Profiles),
                                Program = rule.ApplicationName ?? "",
                                Protocol = DecodeProtocol((int)rule.Protocol),
                                LocalPort = rule.LocalPorts ?? "",
                                RemotePort = rule.RemotePorts ?? "",
                                RemoteAddress = rule.RemoteAddresses ?? ""
                            });
                        }
                        catch
                        {
                            // Skip rules that can't be read
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FirewallService.LoadRules Error: {ex.Message}");
                }
                return results;
            });
        }

        /// <summary>
        /// Toggle a single rule's enabled state via COM.
        /// </summary>
        public Task ToggleRuleAsync(string ruleName, bool enabled)
        {
            return Task.Run(() =>
            {
                try
                {
                    dynamic fwPolicy = GetPolicy();
                    foreach (dynamic rule in fwPolicy.Rules)
                    {
                        try
                        {
                            if ((string)rule.Name == ruleName)
                            {
                                rule.Enabled = enabled;
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FirewallService.ToggleRule Error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Outcome of a group-wide enable/disable so callers can show what actually happened.
        /// </summary>
        public class GroupToggleResult
        {
            public string GroupName { get; set; }
            public int Matched { get; set; }
            public int Changed { get; set; }
            public int Failed { get; set; }
            public string FirstError { get; set; }
            public bool FailedCompletely { get; set; }
            public string Notes { get; set; }
        }

        /// <summary>
        /// Toggle all rules in a group. Matches the raw Grouping string and its resolved display name.
        /// </summary>
        public Task<GroupToggleResult> ToggleGroupAsync(string groupName, bool enabled)
        {
            return Task.Run(() =>
            {
                var result = new GroupToggleResult { GroupName = groupName };
                try
                {
                    dynamic fwPolicy = GetPolicy();
                    foreach (dynamic rule in fwPolicy.Rules)
                    {
                        string ruleGroup;
                        bool wasEnabled;
                        try
                        {
                            ruleGroup = rule.Grouping ?? "";
                            wasEnabled = (bool)rule.Enabled;
                        }
                        catch
                        {
                            continue; // Rule unreadable — skip as before
                        }

                        bool matches = (string.IsNullOrEmpty(ruleGroup) && groupName == "(Ungrouped)")
                                       || GroupMatches(ruleGroup, groupName);
                        if (!matches) continue;

                        result.Matched++;
                        try
                        {
                            rule.Enabled = enabled;
                            if (wasEnabled != enabled) result.Changed++;
                        }
                        catch (Exception ex)
                        {
                            result.Failed++;
                            if (result.FirstError == null) result.FirstError = ex.Message;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.FailedCompletely = true;
                    result.FirstError = ex.Message;
                }
                return result;
            });
        }

        /// <summary>
        /// Get summary counts.
        /// </summary>
        public Task<(int TotalInbound, int EnabledInbound, int TotalOutbound, int EnabledOutbound)> GetSummaryAsync()
        {
            return Task.Run(() =>
            {
                int totalIn = 0, enabledIn = 0, totalOut = 0, enabledOut = 0;
                try
                {
                    dynamic fwPolicy = GetPolicy();
                    foreach (dynamic rule in fwPolicy.Rules)
                    {
                        try
                        {
                            int dir = (int)rule.Direction;
                            bool en = (bool)rule.Enabled;
                            if (dir == 1) { totalIn++; if (en) enabledIn++; }
                            else { totalOut++; if (en) enabledOut++; }
                        }
                        catch { }
                    }
                }
                catch { }
                return (totalIn, enabledIn, totalOut, enabledOut);
            });
        }

        /// <summary>
        /// Check if specific rules match their expected config state (for drift detection).
        /// Returns list of drifted rule names.
        /// </summary>
        public Task<List<string>> CheckDriftAsync(Dictionary<string, string> overrides)
        {
            return Task.Run(() =>
            {
                var drifted = new List<string>();
                if (overrides == null || overrides.Count == 0) return drifted;

                try
                {
                    dynamic fwPolicy = GetPolicy();
                    foreach (dynamic rule in fwPolicy.Rules)
                    {
                        try
                        {
                            string name = (string)rule.Name;
                            if (overrides.TryGetValue(name, out string expected))
                            {
                                bool actual = (bool)rule.Enabled;
                                bool expectedBool = expected == "True";
                                if (actual != expectedBool)
                                {
                                    drifted.Add($"{name} (Expected: {expected}, Actual: {(actual ? "True" : "False")})");
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                return drifted;
            });
        }

        /// <summary>
        /// Adds inbound and outbound block rules for a specific application path.
        /// </summary>
        public Task AddBlockRuleForAppAsync(string appPath, string ruleName)
        {
            return Task.Run(() =>
            {
                try
                {
                    Type ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
                    
                    // Outbound block
                    dynamic ruleOut = Activator.CreateInstance(ruleType);
                    ruleOut.Action = 0; // NET_FW_ACTION_BLOCK
                    ruleOut.Description = "AutoCommand User Block";
                    ruleOut.Direction = 2; // NET_FW_RULE_DIR_OUT
                    ruleOut.Enabled = true;
                    ruleOut.InterfaceTypes = "All";
                    ruleOut.Name = $"{ruleName} (Outbound Block)";
                    ruleOut.ApplicationName = appPath;

                    // Inbound block
                    dynamic ruleIn = Activator.CreateInstance(ruleType);
                    ruleIn.Action = 0; // NET_FW_ACTION_BLOCK
                    ruleIn.Description = "AutoCommand User Block";
                    ruleIn.Direction = 1; // NET_FW_RULE_DIR_IN
                    ruleIn.Enabled = true;
                    ruleIn.InterfaceTypes = "All";
                    ruleIn.Name = $"{ruleName} (Inbound Block)";
                    ruleIn.ApplicationName = appPath;

                    dynamic fwPolicy = GetPolicy();
                    fwPolicy.Rules.Add(ruleOut);
                    fwPolicy.Rules.Add(ruleIn);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to add firewall rule: {ex.Message}", ex);
                }
            });
        }

        private static string DecodeProfile(int profiles)
        {
            var parts = new List<string>();
            if ((profiles & 1) != 0) parts.Add("Domain");
            if ((profiles & 2) != 0) parts.Add("Private");
            if ((profiles & 4) != 0) parts.Add("Public");
            return parts.Count > 0 ? string.Join(", ", parts) : "All";
        }

        private static string DecodeProtocol(int protocol)
        {
            return protocol switch
            {
                6 => "TCP",
                17 => "UDP",
                1 => "ICMPv4",
                58 => "ICMPv6",
                256 => "Any",
                _ => protocol.ToString()
            };
        }
    }
}


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

                            results.Add(new FirewallRuleItem
                            {
                                Name = rule.Name ?? "",
                                DisplayName = rule.Name ?? "",
                                DisplayGroup = string.IsNullOrEmpty((string)rule.Grouping) ? "(Ungrouped)" : (string)rule.Grouping,
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
        /// Toggle all rules in a group.
        /// </summary>
        public Task ToggleGroupAsync(string groupName, bool enabled)
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
                            string ruleGroup = rule.Grouping ?? "";
                            if (string.IsNullOrEmpty(ruleGroup) && groupName == "(Ungrouped)")
                            {
                                rule.Enabled = enabled;
                            }
                            else if (ruleGroup == groupName)
                            {
                                rule.Enabled = enabled;
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FirewallService.ToggleGroup Error: {ex.Message}");
                }
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


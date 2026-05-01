using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AutoCommand.Services
{
    /// <summary>
    /// Firewall profile presets using COM API.
    /// Ported from DeviceMonitorCS — now uses FirewallService instead of PowerShell.
    /// </summary>
    public class FirewallProfileService
    {
        private static FirewallProfileService _instance;
        public static FirewallProfileService Instance => _instance ??= new FirewallProfileService();

        public enum ProfileType
        {
            Custom,
            StrictPublic,
            HomeTrusted,
            GamingMedia,
            ShieldUp
        }

        public async Task ApplyProfile(ProfileType profile)
        {
            switch (profile)
            {
                case ProfileType.StrictPublic:
                    await DisableGroup("File and Printer Sharing");
                    await DisableGroup("Network Discovery");
                    await DisableGroup("Remote Desktop");
                    break;
                case ProfileType.HomeTrusted:
                    await EnableGroup("File and Printer Sharing");
                    await EnableGroup("Network Discovery");
                    break;
                case ProfileType.GamingMedia:
                    await EnableGroup("Network Discovery");
                    await EnableGroup("Cast to Device");
                    break;
                case ProfileType.ShieldUp:
                    await ApplyShieldUp();
                    break;
            }
        }

        private async Task ApplyShieldUp()
        {
            // Disable ALL rules, then re-enable whitelisted groups
            await Task.Run(() =>
            {
                try
                {
                    Type fwPolicyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                    dynamic fwPolicy = Activator.CreateInstance(fwPolicyType);

                    // Pass 1: Disable everything with a group
                    foreach (dynamic rule in fwPolicy.Rules)
                    {
                        try
                        {
                            string group = rule.Grouping ?? "";
                            if (!string.IsNullOrEmpty(group))
                            {
                                rule.Enabled = false;
                            }
                        }
                        catch { }
                    }

                    // Pass 2: Re-enable whitelisted groups
                    var whitelist = new[] { "mDNS", "Core Networking" };
                    foreach (dynamic rule in fwPolicy.Rules)
                    {
                        try
                        {
                            string group = rule.Grouping ?? "";
                            foreach (var wl in whitelist)
                            {
                                if (group.Equals(wl, StringComparison.OrdinalIgnoreCase))
                                {
                                    rule.Enabled = true;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ShieldUp Error: {ex.Message}");
                }
            });
        }

        private Task EnableGroup(string group)
        {
            return FirewallService.Instance.ToggleGroupAsync(group, true);
        }

        private Task DisableGroup(string group)
        {
            return FirewallService.Instance.ToggleGroupAsync(group, false);
        }
    }
}


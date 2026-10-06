using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Helpers;
using AutoCommand.Models;
using System.Text;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class FirewallSettingsView : UserControl, IAiAuditable
    {
        private List<FirewallRuleItem> _currentRules = new();

        public FirewallSettingsView()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadRules();
            await UpdateBaselineStatusAsync();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await LoadRules();
            await UpdateBaselineStatusAsync();
        }

        private async Task UpdateBaselineStatusAsync()
        {
            var info = FirewallBaselineManager.BaselineInfo;
            if (FirewallBaselineManager.IsPaused)
            {
                BaselineStatusText.Text = $"Baseline auto-fix PAUSED until {FirewallBaselineManager.PausedUntilUtc.ToLocalTime():HH:mm}"
                                          + (info == null ? "" : $" — baseline has {info.Value.Count} rules");
            }
            else
            {
                BaselineStatusText.Text = info == null
                    ? "No baseline — apply a profile or click Re-baseline to capture one"
                    : $"Baseline: {info.Value.Count} rules (captured {info.Value.CapturedAt:yyyy-MM-dd HH:mm})";
            }
        }

        private async void RebaselineBtn_Click(object sender, RoutedEventArgs e)
        {
            RebaselineBtn.IsEnabled = false;
            try
            {
                var (count, at) = await FirewallBaselineManager.CaptureBaselineAsync();
                BaselineStatusText.Text = $"Baseline: {count} rules (captured {at:yyyy-MM-dd HH:mm})";
            }
            finally
            {
                RebaselineBtn.IsEnabled = true;
            }
        }

        private async void PauseEnforcementBtn_Click(object sender, RoutedEventArgs e)
        {
            FirewallBaselineManager.PauseForMinutes(15);
            await UpdateBaselineStatusAsync();
        }

        private async void FilterChanged(object sender, RoutedEventArgs e) => await LoadRules();

        private async Task LoadRules()
        {
            int direction = InboundRadio.IsChecked == true ? 1 : 2;
            _currentRules = await FirewallService.Instance.LoadRulesAsync(direction);

            // Enabled rules first (then alphabetical) so the refreshed list reads top-down
            _currentRules = _currentRules.OrderByDescending(r => r.Enabled)
                                         .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                                         .ToList();

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_currentRules);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("DisplayName"));

            RulesGrid.ItemsSource = view;

            int enabled = _currentRules.Count(r => r.Enabled);
            string dir = direction == 1 ? "Inbound" : "Outbound";
            RuleCountText.Text = $"{_currentRules.Count} {dir} rules ({enabled} enabled)";
        }

        private async void ToggleRule_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not FirewallRuleItem rule) return;
            bool newState = !rule.Enabled;
            await FirewallService.Instance.ToggleRuleAsync(rule.Name, newState);
            rule.Enabled = newState;
            // Adopt the change into the baseline so the enforcer doesn't revert it
            FirewallBaselineManager.UpdateRuleState(rule.Name, newState);
        }

        private async void EnableGroup_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not FirewallRuleItem rule) return;
            ProfileStatusText.Text = $"Enabling group '{rule.DisplayGroup}'…";
            var r = await FirewallService.Instance.ToggleGroupAsync(rule.DisplayGroup, true);
            ProfileStatusText.Text = $"Group '{rule.DisplayGroup}': {r.Matched} matched, {r.Changed} changed, {r.Failed} failed"
                                     + (r.FirstError != null ? $" — {r.FirstError}" : "");
            await FirewallBaselineManager.UpdateGroupStateAsync(rule.DisplayGroup, true);
            await LoadRules();
        }

        private async void DisableGroup_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not FirewallRuleItem rule) return;
            ProfileStatusText.Text = $"Disabling group '{rule.DisplayGroup}'…";
            var r = await FirewallService.Instance.ToggleGroupAsync(rule.DisplayGroup, false);
            ProfileStatusText.Text = $"Group '{rule.DisplayGroup}': {r.Matched} matched, {r.Changed} changed, {r.Failed} failed"
                                     + (r.FirstError != null ? $" — {r.FirstError}" : "");
            await FirewallBaselineManager.UpdateGroupStateAsync(rule.DisplayGroup, false);
            await LoadRules();
        }

        private void SaveToConfig_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not FirewallRuleItem rule) return;
            FirewallConfigManager.Instance.SetOverride(rule.Name, rule.Enabled);
            MessageBox.Show($"Saved: {rule.DisplayName} = {(rule.Enabled ? "Enabled" : "Disabled")}",
                "Config Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfileCombo == null || ProfileCombo.SelectedIndex <= 0) return;

            string profileName = ((ComboBoxItem)ProfileCombo.SelectedItem).Content.ToString();

            if (MessageBox.Show($"Apply '{profileName}' profile?\n\nThis will modify your Windows Defender Firewall rules.",
                "Confirm Firewall Profile", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                ProfileCombo.SelectedIndex = 0; // Reset to Custom
                return;
            }

            FirewallProfileService.ProfileType type;
            switch (profileName)
            {
                case "Shield Up": type = FirewallProfileService.ProfileType.ShieldUp; break;
                case "Gaming": type = FirewallProfileService.ProfileType.GamingMedia; break;
                case "Office": type = FirewallProfileService.ProfileType.Office; break;
                case "Home": type = FirewallProfileService.ProfileType.HomeTrusted; break;
                case "Public Strict": type = FirewallProfileService.ProfileType.StrictPublic; break;
                default:
                    ProfileCombo.SelectedIndex = 0;
                    return;
            }

            ProfileCombo.IsEnabled = false;
            RefreshBtn.IsEnabled = false;
            ProfileProgress.Visibility = Visibility.Visible;
            ProfileStatusText.Text = $"Applying '{profileName}' profile…";
            var progress = new Progress<string>(msg => ProfileStatusText.Text = msg);

            try
            {
                ProfileApplyResult result = await FirewallProfileService.Instance.ApplyProfile(type, progress);

                await LoadRules(); // Auto-refresh the grid so the new state is shown immediately

                // The profile's end state is now the expected state — capture it as the
                // baseline the Security Enforcer will enforce against future drift
                int baselineCount = 0;
                try
                {
                    var (count, at) = await FirewallBaselineManager.CaptureBaselineAsync();
                    baselineCount = count;
                    BaselineStatusText.Text = $"Baseline: {count} rules (captured {at:yyyy-MM-dd HH:mm})";
                }
                catch { }

                var sb = new StringBuilder();
                sb.AppendLine($"Profile '{profileName}' applied.");
                if (!string.IsNullOrEmpty(result.Notes)) sb.AppendLine(result.Notes);
                foreach (var g in result.Groups)
                {
                    if (g.GroupName == "Shield Up (block all)") continue; // Covered by the Notes line
                    sb.AppendLine($"• {g.GroupName}: {g.Matched} matched, {g.Changed} changed"
                                  + (g.Failed > 0 ? $", {g.Failed} FAILED" : ""));
                }
                sb.AppendLine($"Total: {result.TotalChanged} rule(s) changed, {result.TotalFailed} failed.");
                if (baselineCount > 0)
                    sb.AppendLine($"Baseline captured with {baselineCount} rules — drift from this state will now be detected and fixed automatically.");
                if (result.TotalMatched == 0) sb.AppendLine("No matching rules were found — no changes were made.");
                if (result.FirstError != null) sb.AppendLine($"First error: {result.FirstError}");

                ProfileStatusText.Text = $"Profile '{profileName}': {result.TotalChanged} rule(s) changed, "
                                         + $"{result.TotalFailed} failed. Rules list refreshed.";

                MessageBox.Show(sb.ToString(), "Firewall Profile Complete", MessageBoxButton.OK,
                    result.TotalFailed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ProfileStatusText.Text = $"Failed to apply '{profileName}': {ex.Message}";
                MessageBox.Show($"Failed to apply profile: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProfileProgress.Visibility = Visibility.Collapsed;
                RefreshBtn.IsEnabled = true;
                // Reset combobox back to custom to allow re-selection later
                ProfileCombo.IsEnabled = true;
                ProfileCombo.SelectedIndex = 0;
            }
        }

        public string GetAuditContext()
        {
            if (_currentRules.Count == 0) return "No firewall rules loaded. Click Refresh first.";
            var sb = new StringBuilder();
            string dir = InboundRadio.IsChecked == true ? "Inbound" : "Outbound";
            sb.AppendLine($"Firewall Rules ({dir}) — {_currentRules.Count} total:");
            int shown = 0;
            foreach (var rule in _currentRules.Where(r => r.Enabled).Take(80))
            {
                sb.AppendLine($"- [{(rule.Enabled ? "ENABLED" : "disabled")}] {rule.DisplayName} | Group: {rule.DisplayGroup} | Action: {rule.Action}");
                shown++;
            }
            if (_currentRules.Count(r => !r.Enabled) > 0)
                sb.AppendLine($"... plus {_currentRules.Count(r => !r.Enabled)} disabled rules omitted.");
            return sb.ToString();
        }
    }
}


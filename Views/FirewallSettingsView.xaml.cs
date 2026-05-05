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
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await LoadRules();

        private async void FilterChanged(object sender, RoutedEventArgs e) => await LoadRules();

        private async Task LoadRules()
        {
            int direction = InboundRadio.IsChecked == true ? 1 : 2;
            _currentRules = await FirewallService.Instance.LoadRulesAsync(direction);

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
        }

        private async void EnableGroup_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not FirewallRuleItem rule) return;
            await FirewallService.Instance.ToggleGroupAsync(rule.DisplayGroup, true);
            await LoadRules();
        }

        private async void DisableGroup_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not FirewallRuleItem rule) return;
            await FirewallService.Instance.ToggleGroupAsync(rule.DisplayGroup, false);
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

            try
            {
                switch (profileName)
                {
                    case "Shield Up":
                        await FirewallProfileService.Instance.ApplyProfile(FirewallProfileService.ProfileType.ShieldUp);
                        break;
                    case "Gaming":
                        await FirewallProfileService.Instance.ApplyProfile(FirewallProfileService.ProfileType.GamingMedia);
                        break;
                    case "Office":
                        await FirewallProfileService.Instance.ApplyProfile(FirewallProfileService.ProfileType.Office);
                        break;
                    case "Home":
                        await FirewallProfileService.Instance.ApplyProfile(FirewallProfileService.ProfileType.HomeTrusted);
                        break;
                    case "Public Strict":
                        await FirewallProfileService.Instance.ApplyProfile(FirewallProfileService.ProfileType.StrictPublic);
                        break;
                }
                await LoadRules();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to apply profile: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Reset combobox back to custom to allow re-selection later
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


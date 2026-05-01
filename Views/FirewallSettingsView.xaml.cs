using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Helpers;
using AutoCommand.Models;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class FirewallSettingsView : UserControl
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

        private async void DirectionChanged(object sender, RoutedEventArgs e) => await LoadRules();

        private async Task LoadRules()
        {
            int direction = InboundRadio.IsChecked == true ? 1 : 2;
            _currentRules = await FirewallService.Instance.LoadRulesAsync(direction);

            // Sort by group then name
            _currentRules = _currentRules
                .OrderBy(r => r.DisplayGroup)
                .ThenBy(r => r.DisplayName)
                .ToList();

            RulesGrid.ItemsSource = _currentRules;

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
            if (ProfileCombo.SelectedIndex <= 0) return; // Custom = no action

            var profile = ProfileCombo.SelectedIndex switch
            {
                1 => FirewallProfileService.ProfileType.StrictPublic,
                2 => FirewallProfileService.ProfileType.HomeTrusted,
                3 => FirewallProfileService.ProfileType.GamingMedia,
                4 => FirewallProfileService.ProfileType.ShieldUp,
                _ => FirewallProfileService.ProfileType.Custom
            };

            if (profile == FirewallProfileService.ProfileType.Custom) return;

            string name = ((ComboBoxItem)ProfileCombo.SelectedItem).Content.ToString();
            if (MessageBox.Show($"Apply '{name}' profile? This will modify firewall rules.",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                ProfileCombo.SelectedIndex = 0;
                return;
            }

            await FirewallProfileService.Instance.ApplyProfile(profile);
            await LoadRules();
            ProfileCombo.SelectedIndex = 0;
        }

        private async void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Reset Windows Firewall to factory defaults?",
                "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            await ProcessRunner.RunAsync("netsh", "advfirewall reset");
            await LoadRules();
        }
    }
}


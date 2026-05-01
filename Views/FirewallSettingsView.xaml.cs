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

        private async void ProfileStrict_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Apply 'Strict Public' profile? This will modify firewall rules.",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            await FirewallProfileService.Instance.ApplyProfile(FirewallProfileService.ProfileType.StrictPublic);
            await LoadRules();
        }

        private async void ProfileDefault_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Apply 'Home Trusted' profile? This will modify firewall rules.",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            await FirewallProfileService.Instance.ApplyProfile(FirewallProfileService.ProfileType.HomeTrusted);
            await LoadRules();
        }
    }
}


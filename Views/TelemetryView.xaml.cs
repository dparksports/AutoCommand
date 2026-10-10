using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Models;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class TelemetryView : UserControl, IAiAuditable
    {
        private readonly TelemetryRulesService _service = new();
        private readonly ObservableCollection<TelemetryRow> _rows = new();

        private bool _isInitialized = false;

        /// <summary>UI row over TelemetryRuleStatus.</summary>
        private sealed class TelemetryRow
        {
            public string AppName { get; set; }
            public string Category { get; set; }
            public string StateText { get; set; }
            public string Evidence { get; set; }
            public TelemetryRuleStatus Status { get; set; }
        }

        public TelemetryView()
        {
            InitializeComponent();
            RulesGrid.ItemsSource = _rows;
            RulesGrid.SelectionChanged += RulesGrid_SelectionChanged;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) return;
            _isInitialized = true;
            await ScanAsync();
        }

        private async void ScanBtn_Click(object sender, RoutedEventArgs e) => await ScanAsync();

        private async Task ScanAsync()
        {
            ScanBtn.IsEnabled = false;
            StatusText.Text = "Scanning installed software against the opt-out knowledge base…";
            try
            {
                var statuses = await _service.EvaluateAllAsync();
                _rows.Clear();
                foreach (var status in statuses)
                {
                    // Undetected apps stay visible but sorted last: users should
                    // see what the KB covers even when it is not installed here
                    _rows.Add(new TelemetryRow
                    {
                        AppName = status.Rule.AppName,
                        Category = status.Rule.Category,
                        StateText = status.Detected ? status.StateText : "Not installed",
                        Evidence = status.Detected ? status.Evidence : status.Evidence,
                        Status = status
                    });
                }
                var detected = statuses.Where(s => s.Detected).ToList();
                var on = detected.Where(s => s.State == TelemetryState.On).ToList();
                StatusText.Text = detected.Count == 0
                    ? "None of the known-telemetry apps are installed on this machine."
                    : $"{detected.Count} detected — {on.Count} still sending usage data.";
            }
            finally
            {
                ScanBtn.IsEnabled = true;
            }
        }

        private void RulesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not TelemetryRow row || row.Status?.Rule == null) return;
            var rule = row.Status.Rule;
            DetailText.Text =
                $"{rule.AppName} — {rule.Mechanism}\n" +
                $"Scope: {rule.Scope}. Risk of opting out: {rule.RiskNote}" +
                (rule.WatchDomains.Count > 0
                    ? $"\nKnown telemetry domains: {string.Join(", ", rule.WatchDomains)}"
                    : "");
        }

        private async void OptOutBtn_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not TelemetryRow row || row.Status?.Rule == null) return;
            var rule = row.Status.Rule;

            if (MessageBox.Show(
                    $"Turn off telemetry for {rule.AppName}?\n\n{rule.Mechanism}\n\n" +
                    $"Risk: {rule.RiskNote}\nThe current setting is saved first and can be reverted.",
                    "Opt out", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes)
                != MessageBoxResult.Yes) return;

            OptOutBtn.IsEnabled = false;
            try
            {
                var (success, message) = await _service.ApplyOptOutAsync(rule);
                MessageBox.Show(message, success ? "Opted out" : "Failed", MessageBoxButton.OK,
                    success ? MessageBoxImage.Information : MessageBoxImage.Error);
                await ScanAsync();
            }
            finally
            {
                OptOutBtn.IsEnabled = true;
            }
        }

        private async void RevertBtn_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not TelemetryRow row || row.Status?.Rule == null) return;

            RevertBtn.IsEnabled = false;
            try
            {
                var (success, message) = await _service.RevertAsync(row.Status.Rule);
                MessageBox.Show(message, success ? "Reverted" : "Revert unavailable", MessageBoxButton.OK,
                    success ? MessageBoxImage.Information : MessageBoxImage.Warning);
                await ScanAsync();
            }
            finally
            {
                RevertBtn.IsEnabled = true;
            }
        }

        private async void VerifyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not TelemetryRow row || row.Status?.Rule == null) return;
            var rule = row.Status.Rule;
            if (rule.WatchDomains.Count == 0)
            {
                MessageBox.Show("No known telemetry domain recorded for this app — nothing to verify.",
                    "Verify", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            VerifyBtn.IsEnabled = false;
            try
            {
                var seen = await _service.CheckRecentDnsLookupAsync(rule.WatchDomains[0]);
                MessageBox.Show(
                    seen
                        ? $"The DNS resolver cache still holds an entry for {rule.WatchDomains[0]} — something contacted it recently (within its TTL). Re-check after the TTL expires or after closing the app."
                        : $"No recent DNS lookup for {rule.WatchDomains[0]} in the resolver cache — consistent with the opt-out working.",
                    "Verification", MessageBoxButton.OK,
                    seen ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            finally
            {
                VerifyBtn.IsEnabled = true;
            }
        }

        public string GetAuditContext()
        {
            var on = _rows.Where(r => r.Status?.Detected == true && r.Status.State == TelemetryState.On).ToList();
            if (on.Count == 0) return "Telemetry Opt-Out: all detected apps are opted out (or none detected).";
            var sb = new System.Text.StringBuilder("Telemetry Opt-Out — apps still sending usage data:");
            foreach (var row in on)
                sb.AppendLine($"- {row.AppName}");
            return sb.ToString();
        }
    }
}

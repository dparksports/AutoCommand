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
    public partial class UpdateControlView : UserControl, IAiAuditable
    {
        private readonly UpdateFreezeService _freezeService = new();
        private readonly ObservableCollection<FreezeRow> _rows = new();

        private bool _isInitialized = false;

        /// <summary>UI row: discovery target + live frozen state.</summary>
        private sealed class FreezeRow
        {
            public string Name { get; set; }
            public string ExePath { get; set; }
            public string Evidence { get; set; }
            public string Vendor { get; set; }
            public string State { get; set; }
            public bool IsFreezable { get; set; }
            public string NotFreezableReason { get; set; }
        }

        public UpdateControlView()
        {
            InitializeComponent();
            TargetsGrid.ItemsSource = _rows;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) return;
            _isInitialized = true;

            // Show already-frozen apps even before a discovery scan
            await RefreshRowsAsync(_freezeService.DiscoverCandidates());
        }

        private async void DiscoverBtn_Click(object sender, RoutedEventArgs e)
        {
            DiscoverBtn.IsEnabled = false;
            StatusText.Text = "Scanning for self-updating apps…";
            try
            {
                var targets = await Task.Run(() => _freezeService.DiscoverCandidates());
                await RefreshRowsAsync(targets);
                StatusText.Text = targets.Count == 0
                    ? "No self-updating apps found in the user profile. Frozen apps from earlier sessions are kept in the list."
                    : $"{targets.Count} app(s) found. Select one and Freeze to pin its version — updates fail until unfrozen.";
            }
            finally
            {
                DiscoverBtn.IsEnabled = true;
            }
        }

        private async Task RefreshRowsAsync(System.Collections.Generic.List<UpdateFreezeTarget> targets)
        {
            _rows.Clear();
            foreach (var target in targets)
            {
                _rows.Add(new FreezeRow
                {
                    Name = target.Name,
                    ExePath = target.ExePath,
                    Evidence = target.IsFreezable
                        ? target.Evidence
                        : $"{target.Evidence} — {target.NotFreezableReason}",
                    Vendor = target.Vendor,
                    State = _freezeService.IsFrozen(target.ExePath) ? "Frozen" : "Unfrozen",
                    IsFreezable = target.IsFreezable,
                    NotFreezableReason = target.NotFreezableReason
                });
            }
            await Task.CompletedTask;
        }

        private async void FreezeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (TargetsGrid.SelectedItem is not FreezeRow row) return;

            if (!row.IsFreezable)
            {
                MessageBox.Show(row.NotFreezableReason, "Cannot freeze", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_freezeService.IsFrozen(row.ExePath)) return;

            if (MessageBox.Show(
                    $"Freeze {row.Name} at its current version?\n\n{row.ExePath}\n\n" +
                    "The exe gets a deny rule for delete/write: the app keeps working, but its updater will fail " +
                    "(it may log update errors) until you unfreeze it here. The original permissions are backed up " +
                    "first and restored on unfreeze.",
                    "Freeze updates", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                != MessageBoxResult.Yes) return;

            FreezeBtn.IsEnabled = false;
            try
            {
                var (success, message) = await _freezeService.FreezeAsync(row.ExePath);
                MessageBox.Show(message, success ? "Frozen" : "Freeze failed", MessageBoxButton.OK,
                    success ? MessageBoxImage.Information : MessageBoxImage.Error);
                await RefreshRowsAsync(_freezeService.DiscoverCandidates());
            }
            finally
            {
                FreezeBtn.IsEnabled = true;
            }
        }

        private async void ThawBtn_Click(object sender, RoutedEventArgs e)
        {
            if (TargetsGrid.SelectedItem is not FreezeRow row) return;
            if (!_freezeService.IsFrozen(row.ExePath))
            {
                MessageBox.Show($"{row.Name} is not frozen.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(
                    $"Unfreeze {row.Name} and restore its original permissions?\nUpdates will work again.",
                    "Unfreeze", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            ThawBtn.IsEnabled = false;
            try
            {
                var (success, message) = await _freezeService.ThawAsync(row.ExePath);
                MessageBox.Show(message, success ? "Unfrozen" : "Unfreeze failed", MessageBoxButton.OK,
                    success ? MessageBoxImage.Information : MessageBoxImage.Error);
                await RefreshRowsAsync(_freezeService.DiscoverCandidates());
            }
            finally
            {
                ThawBtn.IsEnabled = true;
            }
        }

        public string GetAuditContext()
        {
            var frozen = _rows.Where(r => r.State == "Frozen").ToList();
            if (frozen.Count == 0) return "Update Control: no apps currently frozen.";
            var sb = new System.Text.StringBuilder("Update Control — frozen apps (updates blocked):");
            foreach (var row in frozen)
                sb.AppendLine($"- {row.Name}: {row.ExePath}");
            return sb.ToString();
        }
    }
}

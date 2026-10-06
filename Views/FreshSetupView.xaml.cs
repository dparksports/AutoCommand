using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class FreshSetupView : UserControl
    {
        private readonly ObservableCollection<SetupStepItem> _items = new ObservableCollection<SetupStepItem>();
        private bool _running;

        public FreshSetupView()
        {
            InitializeComponent();
            StepsList.ItemsSource = _items;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0)
            {
                foreach (var step in FreshSetupService.Steps)
                    _items.Add(new SetupStepItem(step));
                await VerifyAllAsync();
            }
        }

        private async Task VerifyAllAsync()
        {
            SetupStatusText.Text = "Checking current state…";
            var checks = _items.Select(async item =>
            {
                var (ok, detail) = await item.Step.Verify();
                item.SetResult(ok, detail);
            });
            await Task.WhenAll(checks);
            int okCount = _items.Count(i => i.IsOk);
            SetupStatusText.Text = $"{okCount} of {_items.Count} steps already applied. Press Apply to complete the rest.";
        }

        private async void ApplyAllBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_running) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("This will apply ALL of the following in one pass:\n");
            foreach (var item in _items)
                sb.AppendLine($"• {item.Title}");
            sb.AppendLine("\nDevices, firewall rules, apps and system settings will be modified. Continue?");

            if (MessageBox.Show(sb.ToString(), "Confirm One-Click Setup",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            _running = true;
            ApplyAllBtn.IsEnabled = false;
            OverallProgress.Maximum = _items.Count;
            OverallProgress.Value = 0;

            int succeeded = 0;
            var failed = new List<string>();
            var progress = new Progress<string>(msg => SetupStatusText.Text = msg);

            try
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var item = _items[i];
                    item.SetRunning();
                    OverallProgress.Value = i;

                    try
                    {
                        var (success, detail) = await item.Step.Apply(progress);
                        item.SetResult(success, detail);
                        if (success) succeeded++;
                        else failed.Add(item.Title);
                    }
                    catch (Exception ex)
                    {
                        item.SetResult(false, $"error: {ex.Message}");
                        failed.Add(item.Title);
                    }

                    OverallProgress.Value = i + 1;
                }

                // Bird's-eye confirmation: re-read live state for every step
                await VerifyAllAsync();

                SetupStatusText.Text = $"Complete — {succeeded}/{_items.Count} steps applied"
                                       + (failed.Count > 0 ? $"; failed: {string.Join(", ", failed)}" : "")
                                       + ". Reboot recommended.";

                MessageBox.Show(
                    $"Applied {succeeded} of {_items.Count} steps."
                    + (failed.Count > 0 ? $"\n\nFailed:\n- {string.Join("\n- ", failed)}" : "")
                    + "\n\nA reboot is recommended so firewall, IPv6, UAC and device changes fully take effect.",
                    "One-Click Setup Complete",
                    MessageBoxButton.OK,
                    failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            finally
            {
                _running = false;
                ApplyAllBtn.IsEnabled = true;
            }
        }

        /// <summary>Bindable wrapper around a SetupStep for the checklist UI.</summary>
        public class SetupStepItem : INotifyPropertyChanged
        {
            private static readonly Brush GrayBrush = new SolidColorBrush(Color.FromRgb(0x8F, 0xAE, 0xCB));
            private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F));
            private static readonly Brush AmberBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31));
            private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A));

            public SetupStep Step { get; }

            public SetupStepItem(SetupStep step)
            {
                Step = step;
                _icon = "•";
                _statusText = "Checking…";
                _statusBrush = GrayBrush;
            }

            public string Title => Step.Title;
            public string Description => Step.Description;

            private string _icon;
            public string Icon
            {
                get => _icon;
                private set { _icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
            }

            private string _statusText;
            public string StatusText
            {
                get => _statusText;
                private set { _statusText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText))); }
            }

            private Brush _statusBrush;
            public Brush StatusBrush
            {
                get => _statusBrush;
                private set { _statusBrush = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush))); }
            }

            public bool IsOk { get; private set; }

            public event PropertyChangedEventHandler PropertyChanged;

            public void SetRunning()
            {
                Icon = "⏳";
                StatusText = "Running…";
                StatusBrush = GrayBrush;
            }

            public void SetResult(bool ok, string detail)
            {
                IsOk = ok;
                Icon = ok ? "✓" : "⚠";
                StatusText = detail ?? (ok ? "OK" : "not applied");
                StatusBrush = ok ? GreenBrush : AmberBrush;
            }
        }
    }
}

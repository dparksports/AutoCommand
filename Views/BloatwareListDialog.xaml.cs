using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoCommand.Helpers;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    /// <summary>
    /// Editor for the bloatware list. Edits apply immediately through
    /// BloatwareConfigManager (saved to bloatware_config.json); the dialog
    /// itself is stateless — every change reloads from the config so the
    /// view always shows persisted truth.
    /// </summary>
    public partial class BloatwareListDialog : Window
    {
        private sealed class DefaultEntry
        {
            public string Pattern { get; set; }
            public string Label { get; set; }
            public bool IsEnabled { get; set; }
        }

        /// <summary>Friendly labels for the compiled-in defaults; patterns
        /// without an entry fall back to the raw pattern text, so defaults
        /// added in future versions still show up here automatically.</summary>
        private static readonly Dictionary<string, string> DefaultLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Outlook"] = "Outlook (new Outlook for Windows)",
            ["Xbox"] = "Xbox",
            ["Phone"] = "Phone Link",
            ["Family"] = "Family Safety",
            ["Copilot"] = "Copilot",
            ["WindowsFeedbackHub"] = "Feedback Hub",
            ["GetHelp"] = "Get Help",
            ["BingNews"] = "Microsoft News",
            ["Teams"] = "Teams",
            ["Todos"] = "To Do",
            ["CrossDevice"] = "Mobile Devices (Cross-Device Host)",
            ["PowerAutomate"] = "Power Automate",
            ["QuickAssist"] = "Quick Assist",
            ["Solitaire"] = "Solitaire & Casual Games",
            ["WindowsCalendar"] = "Windows Calendar",
            ["WindowsCalculator"] = "Calculator",
            ["WindowsSoundRecorder"] = "Sound Recorder",
            ["WebExperience"] = "Web Experience Pack",
            ["WindowsTerminal"] = "Terminal",
            ["WidgetsPlatformRuntime"] = "Widgets Platform Runtime",
            ["DevHome"] = "Dev Home",
            ["RemoteDesktop"] = "Remote Desktop (Store client)",
            ["OneDrive"] = "OneDrive (Win32 app)",
        };

        private readonly ObservableCollection<DefaultEntry> _defaults = new();
        private readonly ObservableCollection<string> _customPackages = new();
        private readonly ObservableCollection<string> _customPatterns = new();

        public BloatwareListDialog()
        {
            InitializeComponent();
            LoadState();
        }

        private void LoadState()
        {
            var (disabled, packages, patterns) = BloatwareConfig.Instance.Snapshot();

            _defaults.Clear();
            foreach (var pattern in AppManagerService.BloatwarePatterns)
            {
                _defaults.Add(new DefaultEntry
                {
                    Pattern = pattern,
                    Label = DefaultLabels.TryGetValue(pattern, out var label) ? label : pattern,
                    IsEnabled = !disabled.Contains(pattern)
                });
            }
            DefaultsList.ItemsSource = _defaults;

            _customPackages.Clear();
            foreach (var p in packages) _customPackages.Add(p);
            CustomPackagesList.ItemsSource = _customPackages;

            _customPatterns.Clear();
            foreach (var p in patterns) _customPatterns.Add(p);
            CustomPatternsList.ItemsSource = _customPatterns;

            // Stale until re-run against the (possibly edited) list
            PreviewList.ItemsSource = null;
            PreviewCountText.Text = "See exactly what the current list would remove.";
        }

        private void DefaultCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is DefaultEntry entry)
            {
                if (cb.IsChecked == true) BloatwareConfig.Instance.EnableDefault(entry.Pattern);
                else BloatwareConfig.Instance.DisableDefault(entry.Pattern);
            }
        }

        private void AddCustomPackageBtn_Click(object sender, RoutedEventArgs e) => AddCustomPackage();

        private void NewPackageBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddCustomPackage();
        }

        private void AddCustomPackage()
        {
            string name = NewPackageBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            BloatwareConfig.Instance.AddCustomPackage(name);
            NewPackageBox.Clear();
            LoadState();
        }

        private void RemoveCustomPackageBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is string name)
            {
                BloatwareConfig.Instance.RemoveCustomPackage(name);
                LoadState();
            }
        }

        private void AddCustomPatternBtn_Click(object sender, RoutedEventArgs e) => AddCustomPattern();

        private void NewPatternBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddCustomPattern();
        }

        private void AddCustomPattern()
        {
            string pattern = NewPatternBox.Text?.Trim();
            if (string.IsNullOrEmpty(pattern)) return;

            BloatwareConfig.Instance.AddCustomPattern(pattern);
            NewPatternBox.Clear();
            LoadState();
        }

        private void RemoveCustomPatternBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is string pattern)
            {
                BloatwareConfig.Instance.RemoveCustomPattern(pattern);
                LoadState();
            }
        }

        private async void PreviewBtn_Click(object sender, RoutedEventArgs e)
        {
            PreviewBtn.IsEnabled = false;
            PreviewCountText.Text = "Scanning…";
            try
            {
                var matches = await AppManagerService.Instance.FindBloatwareAsync();
                PreviewList.ItemsSource = matches
                    .Select(m => $"{m.Name}   [{m.FullName}]")
                    .ToList();
                PreviewCountText.Text = $"{matches.Count} package(s) currently match the bloatware list.";
            }
            catch (Exception ex)
            {
                PreviewCountText.Text = $"Preview failed: {ex.Message}";
            }
            finally
            {
                PreviewBtn.IsEnabled = true;
            }
        }

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(this,
                    "Reset to the built-in default bloatware list? All custom apps and patterns will be removed, and disabled defaults re-enabled.",
                    "Reset Bloatware List", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                BloatwareConfig.Instance.Reset();
                LoadState();
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}

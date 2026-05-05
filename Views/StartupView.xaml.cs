using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Helpers;
using AutoCommand.Models;

namespace AutoCommand.Views
{
    public partial class StartupView : UserControl, IAiAuditable
    {
        private System.Collections.Generic.List<StartupItem> _items = new();
        public StartupView()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadItems();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await LoadItems();

        private async Task LoadItems()
        {
            _items = await Task.Run(() => RegistryHelper.GetStartupItems());
            Dispatcher.Invoke(() => StartupGrid.ItemsSource = _items);
        }

        public string GetAuditContext()
        {
            if (_items.Count == 0) return "No startup entries found.";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Startup Registry Entries ({_items.Count} total):");
            foreach (var item in _items)
                sb.AppendLine($"- [{item.Location}] {item.Name} → {item.Value}");
            return sb.ToString();
        }

        private async void DeleteEntry_Click(object sender, RoutedEventArgs e)
        {
            if (StartupGrid.SelectedItem is not StartupItem item) return;

            if (MessageBox.Show($"Delete startup entry '{item.Name}' from {item.Location}?\n\nTarget: {item.Value}",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            await Task.Run(() => RegistryHelper.DeleteStartupItem(item));
            await LoadItems();
        }
    }
}

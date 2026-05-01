using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Helpers;
using AutoCommand.Models;

namespace AutoCommand.Views
{
    public partial class StartupView : UserControl
    {
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
            var items = await Task.Run(() => RegistryHelper.GetStartupItems());
            Dispatcher.Invoke(() => StartupGrid.ItemsSource = items);
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

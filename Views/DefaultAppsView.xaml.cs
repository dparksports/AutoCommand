using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Models;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class DefaultAppsView : UserControl, IAiAuditable
    {
        private readonly AppManagerService _appService = new AppManagerService();

        public DefaultAppsView()
        {
            InitializeComponent();
        }

        public string GetAuditContext()
        {
            var apps = AppsDataGrid.ItemsSource as IEnumerable<AppPackageItem>;
            if (apps == null) return "No application data loaded.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Installed Windows Appx Packages:");
            foreach (var app in apps)
            {
                sb.AppendLine($"- {app.Name} (Version: {app.Version}, Signature: {app.SignatureStatus}, Signer: {app.SignerCertificate})");
            }
            return sb.ToString();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshAppsList();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAppsList();
        }

        private async System.Threading.Tasks.Task RefreshAppsList()
        {
            AppsDataGrid.ItemsSource = null;
            var apps = await _appService.GetInstalledAppsAsync();
            AppsDataGrid.ItemsSource = apps;
        }

        private async void UninstallBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AppPackageItem app)
            {
                var result = MessageBox.Show($"Are you sure you want to uninstall {app.Name}?\n\nFull Name: {app.FullName}", 
                    "Confirm Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    btn.IsEnabled = false;
                    bool success = await _appService.UninstallAppAsync(app.FullName);
                    if (success)
                    {
                        MessageBox.Show($"{app.Name} uninstalled successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        await RefreshAppsList();
                    }
                    else
                    {
                        MessageBox.Show($"Failed to uninstall {app.Name}. It might be a system-protected package.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        btn.IsEnabled = true;
                    }
                }
            }
        }
    }
}

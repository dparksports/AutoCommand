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

        private async void FilterChanged(object sender, RoutedEventArgs e)
        {
            // Fires during InitializeComponent before the checkbox exists — guard it
            if (SystemComponentsCheck == null) return;
            await RefreshAppsList();
        }

        private async void RemoveBloatwareBtn_Click(object sender, RoutedEventArgs e)
        {
            RemoveBloatwareBtn.IsEnabled = false;
            AppCountText.Text = "Scanning for bloatware packages…";

            try
            {
                var result = await _appService.RemoveBloatwareAsync(
                    new Progress<string>(msg => AppCountText.Text = msg),
                    async matches =>
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"The following {matches.Count} package(s) will be removed:\n");
                        foreach (var app in matches)
                            sb.AppendLine($"• {app.Name}  ({app.FullName})");
                        sb.AppendLine("\nContinue?");

                        return MessageBox.Show(sb.ToString(), "Confirm Bloatware Removal",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                    });

                if (result.Removed < 0)
                {
                    // User cancelled the confirmation
                    return;
                }

                if (result.Matched.Count == 0)
                {
                    MessageBox.Show("No installed packages matching Outlook / Xbox / Family / Phone were found.",
                        "Nothing to Remove", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (result.Failed.Count == 0)
                    MessageBox.Show($"Removed {result.Removed} of {result.Matched.Count} package(s) successfully.",
                        "Bloatware Removal Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show($"Removed {result.Removed} of {result.Matched.Count} package(s).\n\nFailed (may be system-protected or managed):\n- "
                                    + string.Join("\n- ", result.Failed),
                        "Bloatware Removal Finished", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Bloatware removal failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RemoveBloatwareBtn.IsEnabled = true;
                await RefreshAppsList();
            }
        }

        private async System.Threading.Tasks.Task RefreshAppsList()
        {
            AppsDataGrid.ItemsSource = null;
            var apps = await _appService.GetInstalledAppsAsync(SystemComponentsCheck.IsChecked == true);
            AppsDataGrid.ItemsSource = apps;
            AppCountText.Text = $"{apps.Count} apps listed"
                                + (SystemComponentsCheck.IsChecked == true ? " (incl. system components)" : " — matches Windows Settings → Installed apps");
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

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Models;
using AutoCommand.Sdk;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class PluginsView : UserControl, IAiAuditable
    {
        private readonly PluginLoaderService _pluginService = new PluginLoaderService();

        public PluginsView()
        {
            InitializeComponent();
        }

        public string GetAuditContext()
        {
            var plugins = _pluginService.LoadedPlugins;
            if (plugins == null || plugins.Count == 0) return "No plugins loaded.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Loaded Plugins:");
            foreach (var p in plugins)
            {
                sb.AppendLine($"- {p.Name} v{p.Version} by {p.Author}: {p.Description}");
            }
            return sb.ToString();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshPluginsList();
        }

        private async void ReloadBtn_Click(object sender, RoutedEventArgs e)
        {
            await RefreshPluginsList();
        }

        private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }

        private async System.Threading.Tasks.Task RefreshPluginsList()
        {
            PluginsDataGrid.ItemsSource = null;
            await _pluginService.LoadPluginsAsync();
            PluginsDataGrid.ItemsSource = _pluginService.LoadedPlugins;

            if (_pluginService.CompileErrors.Count > 0)
            {
                CompileErrorsPanel.Visibility = Visibility.Visible;
                ErrorsList.ItemsSource = _pluginService.CompileErrors;
            }
            else
            {
                CompileErrorsPanel.Visibility = Visibility.Collapsed;
                ErrorsList.ItemsSource = null;
            }
        }

        private async void RunPluginBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is IPlugin plugin)
            {
                try
                {
                    btn.IsEnabled = false;
                    await plugin.ExecuteAsync();
                    MessageBox.Show($"Plugin '{plugin.Name}' executed successfully.", "Plugin Execution", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error executing plugin '{plugin.Name}':\n{ex.Message}", "Plugin Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AutoCommand.Helpers;
using AutoCommand.Models;

namespace AutoCommand.Views
{
    public partial class ConnectionsView : UserControl
    {
        private List<NetworkConnectionItem> _allConnections = new();
        private DispatcherTimer _liveTimer;

        public ConnectionsView()
        {
            InitializeComponent();
            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _liveTimer.Tick += async (s, e) => await LoadConnections();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadConnections();
        }

        private void LiveViewChanged(object sender, RoutedEventArgs e)
        {
            if (LiveViewCheck.IsChecked == true)
                _liveTimer.Start();
            else
                _liveTimer.Stop();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await LoadConnections();

        private void FilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

        private async Task LoadConnections()
        {
            _allConnections.Clear();

            await Task.Run(() =>
            {
                try
                {
                    var tcp = NetworkApiHelper.GetActiveTcpConnections();
                    var udp = NetworkApiHelper.GetActiveUdpConnections();
                    _allConnections.AddRange(tcp);
                    _allConnections.AddRange(udp);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => MessageBox.Show($"Failed to load connections: {ex.Message}"));
                }
            });

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (TcpRadio == null || ConnectionsGrid == null || ConnectionCountText == null) return;
            IEnumerable<NetworkConnectionItem> filtered = _allConnections;

            if (TcpRadio.IsChecked == true)
                filtered = filtered.Where(c => c.Protocol == "TCP");
            else if (UdpRadio.IsChecked == true)
                filtered = filtered.Where(c => c.Protocol == "UDP");

            var list = filtered.OrderBy(c => c.ProcessName).ThenBy(c => c.Protocol).ToList();

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(list);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("ProcessName"));

            ConnectionsGrid.ItemsSource = view;
            ConnectionCountText.Text = $"{list.Count} connections " +
                $"({list.Count(c => c.Protocol == "TCP")} TCP, {list.Count(c => c.Protocol == "UDP")} UDP)";
        }

        private async void KillProcess_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionsGrid.SelectedItem is not NetworkConnectionItem item) return;

            if (item.ProcessId == 0)
            {
                MessageBox.Show("Cannot kill System/Idle process.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"Kill process '{item.ProcessName}' (PID {item.ProcessId})?\nThis forcefully terminates the application.",
                "Confirm Kill", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                var proc = Process.GetProcessById(item.ProcessId);
                proc.Kill();
                await Task.Delay(500); // Give it a moment to release ports
                await LoadConnections();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to kill process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

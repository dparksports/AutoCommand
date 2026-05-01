using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AutoCommand.Models;
using AutoCommand.Services;

namespace AutoCommand.Views
{
    public partial class TasksView : UserControl
    {
        private List<ScheduledTaskItem> _allTasks = new();

        public TasksView()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadTasks();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await LoadTasks();

        private async Task LoadTasks()
        {
            _allTasks = await TaskSchedulerService.Instance.LoadTasksAsync();
            ApplyFilter();
        }

        private void FilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            if (RunningRadio == null || TasksGrid == null || TaskCountText == null) return;
            IEnumerable<ScheduledTaskItem> filtered = _allTasks;

            if (RunningRadio.IsChecked == true)
                filtered = filtered.Where(t => t.State == "Running");
            else if (ReadyRadio.IsChecked == true)
                filtered = filtered.Where(t => t.State == "Ready");
            else if (DisabledRadio.IsChecked == true)
                filtered = filtered.Where(t => t.State == "Disabled");

            var list = filtered.OrderBy(t => t.TaskPath).ThenBy(t => t.TaskName).ToList();
            TasksGrid.ItemsSource = list;
            TaskCountText.Text = $"{list.Count} tasks" +
                (AllRadio.IsChecked == true
                    ? $" ({_allTasks.Count(t => t.State == "Running")} running, {_allTasks.Count(t => t.State == "Ready")} ready, {_allTasks.Count(t => t.State == "Disabled")} disabled)"
                    : "");
        }

        private string GetSelectedTaskFullPath()
        {
            if (TasksGrid.SelectedItem is not ScheduledTaskItem task) return null;
            // Reconstruct the full path for COM API
            string path = task.TaskPath;
            if (path == "\\") return "\\" + task.TaskName;
            if (path == "Windows System") return $@"\Microsoft\Windows\{task.TaskName}";
            if (!path.StartsWith("\\")) path = $@"\Microsoft\Windows\{path}";
            return path + "\\" + task.TaskName;
        }

        private async void DisableTask_Click(object sender, RoutedEventArgs e)
        {
            string path = GetSelectedTaskFullPath();
            if (path == null) return;
            try
            {
                await TaskSchedulerService.Instance.SetTaskEnabledAsync(path, false);
                await LoadTasks();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to disable: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void EnableTask_Click(object sender, RoutedEventArgs e)
        {
            string path = GetSelectedTaskFullPath();
            if (path == null) return;
            try
            {
                await TaskSchedulerService.Instance.SetTaskEnabledAsync(path, true);
                await LoadTasks();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to enable: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void RunTask_Click(object sender, RoutedEventArgs e)
        {
            string path = GetSelectedTaskFullPath();
            if (path == null) return;
            try
            {
                await TaskSchedulerService.Instance.RunTaskAsync(path);
                await Task.Delay(500);
                await LoadTasks();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to run: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void StopTask_Click(object sender, RoutedEventArgs e)
        {
            string path = GetSelectedTaskFullPath();
            if (path == null) return;
            try
            {
                await TaskSchedulerService.Instance.StopTaskAsync(path);
                await LoadTasks();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to stop: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (TasksGrid.SelectedItem is not ScheduledTaskItem task) return;
            if (MessageBox.Show($"Delete task '{task.TaskName}'? This cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            string path = GetSelectedTaskFullPath();
            if (path == null) return;
            try
            {
                await TaskSchedulerService.Instance.DeleteTaskAsync(path);
                await LoadTasks();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}


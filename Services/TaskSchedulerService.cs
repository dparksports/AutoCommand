using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using AutoCommand.Models;

namespace AutoCommand.Services
{
    /// <summary>
    /// Native Task Scheduler management via COM (taskschd.dll).
    /// Replaces all schtasks.exe calls and PowerShell Enable/Disable-ScheduledTask.
    /// Uses dynamic COM — no COM reference DLL needed.
    /// </summary>
    public class TaskSchedulerService
    {
        private static TaskSchedulerService _instance;
        public static TaskSchedulerService Instance => _instance ??= new TaskSchedulerService();

        private dynamic GetService()
        {
            Type tsType = Type.GetTypeFromProgID("Schedule.Service");
            if (tsType == null)
                throw new InvalidOperationException("Task Scheduler COM component not available.");
            dynamic ts = Activator.CreateInstance(tsType);
            ts.Connect();
            return ts;
        }

        /// <summary>
        /// Load all scheduled tasks recursively via COM.
        /// </summary>
        public Task<List<ScheduledTaskItem>> LoadTasksAsync()
        {
            return Task.Run(() =>
            {
                var results = new List<ScheduledTaskItem>();
                try
                {
                    dynamic ts = GetService();
                    dynamic rootFolder = ts.GetFolder("\\");
                    EnumerateFolder(rootFolder, results);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TaskSchedulerService.LoadTasks Error: {ex.Message}");
                }
                return results;
            });
        }

        private void EnumerateFolder(dynamic folder, List<ScheduledTaskItem> results)
        {
            try
            {
                // Get tasks (1 = include hidden)
                dynamic tasks = folder.GetTasks(1);
                foreach (dynamic task in tasks)
                {
                    try
                    {
                        string fullPath = (string)task.Path;
                        string name = task.Name;
                        string folderPath = "\\";
                        
                        int lastSlash = fullPath.LastIndexOf('\\');
                        if (lastSlash > 0)
                        {
                            folderPath = fullPath.Substring(0, lastSlash);
                            // Simplify Microsoft\Windows paths
                            if (folderPath.StartsWith(@"\Microsoft\Windows"))
                            {
                                folderPath = folderPath.Substring(@"\Microsoft\Windows".Length);
                                if (folderPath.StartsWith("\\")) folderPath = folderPath.Substring(1);
                                if (string.IsNullOrEmpty(folderPath)) folderPath = "Windows System";
                            }
                        }
                        else if (lastSlash == 0)
                        {
                            folderPath = "\\";
                        }

                        // Get action info
                        string actionStr = "";
                        try
                        {
                            dynamic def = task.Definition;
                            dynamic actions = def.Actions;
                            if (actions.Count > 0)
                            {
                                dynamic firstAction = actions.Item(1); // 1-based index
                                actionStr = firstAction.Path ?? "";
                            }
                        }
                        catch { }

                        // Get user info
                        string user = "";
                        try
                        {
                            user = task.Definition.Principal.UserId ?? "";
                        }
                        catch { }

                        int stateInt = (int)task.State;
                        string state = stateInt switch
                        {
                            0 => "Unknown",
                            1 => "Disabled",
                            2 => "Queued",
                            3 => "Ready",
                            4 => "Running",
                            _ => "Unknown"
                        };

                        results.Add(new ScheduledTaskItem
                        {
                            TaskName = name,
                            TaskPath = folderPath,
                            State = state,
                            Action = actionStr,
                            User = user
                        });
                    }
                    catch { }
                }

                // Recurse subfolders
                dynamic subFolders = folder.GetFolders(0);
                foreach (dynamic sub in subFolders)
                {
                    EnumerateFolder(sub, results);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnumerateFolder Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Enable or disable a task.
        /// </summary>
        public Task SetTaskEnabledAsync(string taskPath, bool enabled)
        {
            return Task.Run(() =>
            {
                try
                {
                    dynamic ts = GetService();
                    dynamic rootFolder = ts.GetFolder("\\");
                    dynamic task = rootFolder.GetTask(taskPath);
                    task.Enabled = enabled;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SetTaskEnabled Error: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>
        /// Run a task.
        /// </summary>
        public Task RunTaskAsync(string taskPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    dynamic ts = GetService();
                    dynamic rootFolder = ts.GetFolder("\\");
                    dynamic task = rootFolder.GetTask(taskPath);
                    task.Run(null);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"RunTask Error: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>
        /// Stop a running task.
        /// </summary>
        public Task StopTaskAsync(string taskPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    dynamic ts = GetService();
                    dynamic rootFolder = ts.GetFolder("\\");
                    dynamic task = rootFolder.GetTask(taskPath);
                    task.Stop(0);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"StopTask Error: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>
        /// Delete a task.
        /// </summary>
        public Task DeleteTaskAsync(string taskPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    dynamic ts = GetService();
                    // Get the parent folder
                    int lastSlash = taskPath.LastIndexOf('\\');
                    string folderPath = lastSlash > 0 ? taskPath.Substring(0, lastSlash) : "\\";
                    string taskName = taskPath.Substring(lastSlash + 1);

                    dynamic folder = ts.GetFolder(folderPath);
                    folder.DeleteTask(taskName, 0);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DeleteTask Error: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>
        /// Check if a specific task is enabled (for privacy toggle).
        /// </summary>
        public bool IsTaskEnabled(string taskPath)
        {
            try
            {
                dynamic ts = GetService();
                dynamic rootFolder = ts.GetFolder("\\");
                dynamic task = rootFolder.GetTask(taskPath);
                return (int)task.State != 1; // 1 = Disabled
            }
            catch
            {
                return false;
            }
        }
    }
}


using System;
using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AutoCommand.Services
{
    /// <summary>
    /// Attribution for a taskhostw.exe instance: the instance GUID parsed from
    /// its command line, correlated with the Task Scheduler operational log
    /// (which names the scheduled task and its user context).
    /// </summary>
    public class TaskLaunchInfo
    {
        public string InstanceGuid;
        public string TaskPath;       // e.g. "\Microsoft\Windows\Foo\Bar"
        public string UserContext;    // account the task ran as
        public int ProcessId;         // taskhostw PID (from the Sysmon process-create event)
        public string CommandLine;
        public DateTime? StartedAt;
        public DateTime? CompletedAt;
    }

    /// <summary>
    /// Answers "who/what/why" for taskhostw processes: Task Scheduler history
    /// (event 100/102) is correlated with Sysmon process-create events (event 1)
    /// via the task-instance GUID that taskhostw carries on its command line
    /// (e.g. "taskhostw.exe {222A245B-E637-4AE9-A93F-A59CA119A75E}").
    ///
    /// The Task Scheduler operational channel is disabled on many systems; it is
    /// enabled here once (requires admin) so future taskhostw launches can be
    /// attributed. Launches before that are unrecoverable. Disabling a task for
    /// good is handled by TaskSchedulerService.SetTaskEnabledAsync.
    /// </summary>
    public class TaskAttributionService
    {
        private const string TaskSchedulerLog = "Microsoft-Windows-TaskScheduler/Operational";
        private const string SysmonLog = "Microsoft-Windows-Sysmon/Operational";
        private static readonly Regex InstanceGuidRegex = new Regex(@"\{([0-9A-Fa-f\-]{36})\}", RegexOptions.Compiled);

        private EventLogWatcher _sysmonProcessWatcher;   // Sysmon EID1 — taskhostw spawns
        private EventLogWatcher _taskInstanceWatcher;    // TaskScheduler EID100/102
        private readonly ConcurrentDictionary<int, TaskLaunchInfo> _byPid = new ConcurrentDictionary<int, TaskLaunchInfo>();
        private readonly ConcurrentDictionary<string, TaskLaunchInfo> _byGuid = new ConcurrentDictionary<string, TaskLaunchInfo>(StringComparer.OrdinalIgnoreCase);

        public event Action<string> OnError;

        /// <summary>False when the history channel could not be enabled (attribution then degrades to GUID-only).</summary>
        public bool HistoryChannelEnabled { get; private set; }

        public void Start()
        {
            EnableHistoryChannel();

            try
            {
                var query = new EventLogQuery(SysmonLog, PathType.LogName, "*[System[(EventID=1)]]");
                _sysmonProcessWatcher = new EventLogWatcher(query);
                _sysmonProcessWatcher.EventRecordWritten += Sysmon_ProcessCreate;
                _sysmonProcessWatcher.Enabled = true;
            }
            catch (EventLogNotFoundException)
            {
                OnError?.Invoke("Sysmon is not installed — taskhostw instances cannot be detected.");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Failed to watch Sysmon process events: {ex.Message}");
            }

            try
            {
                var query = new EventLogQuery(TaskSchedulerLog, PathType.LogName, "*[System[(EventID=100 or EventID=102)]]");
                _taskInstanceWatcher = new EventLogWatcher(query);
                _taskInstanceWatcher.EventRecordWritten += TaskScheduler_InstanceRecorded;
                _taskInstanceWatcher.Enabled = true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Failed to watch the Task Scheduler log: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (_sysmonProcessWatcher != null)
            {
                _sysmonProcessWatcher.Enabled = false;
                _sysmonProcessWatcher.Dispose();
                _sysmonProcessWatcher = null;
            }
            if (_taskInstanceWatcher != null)
            {
                _taskInstanceWatcher.Enabled = false;
                _taskInstanceWatcher.Dispose();
                _taskInstanceWatcher = null;
            }
        }

        /// <summary>Attribution for a taskhostw PID, or null when unknown/not a task host.</summary>
        public TaskLaunchInfo GetTaskForPid(int pid) =>
            _byPid.TryGetValue(pid, out var info) ? info : null;

        private void EnableHistoryChannel()
        {
            try
            {
                using var config = new EventLogConfiguration(TaskSchedulerLog);
                if (!config.IsEnabled)
                {
                    config.IsEnabled = true;
                    config.SaveChanges();
                }
                HistoryChannelEnabled = true;
            }
            catch (Exception ex)
            {
                HistoryChannelEnabled = false;
                OnError?.Invoke($"Task Scheduler history could not be enabled: {ex.Message}");
            }
        }

        private void Sysmon_ProcessCreate(object sender, EventRecordWrittenEventArgs e)
        {
            try
            {
                if (e.EventRecord == null) return;

                var doc = XDocument.Parse(e.EventRecord.ToXml());
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
                var eventData = doc.Root?.Element(ns + "EventData");
                if (eventData == null) return;

                string image = null, commandLine = null, processId = null;
                foreach (var data in eventData.Elements(ns + "Data"))
                {
                    string name = data.Attribute("Name")?.Value;
                    if (name == "Image") image = data.Value;
                    else if (name == "CommandLine") commandLine = data.Value;
                    else if (name == "ProcessId") processId = data.Value;
                }

                // taskhostw hosts scheduled tasks; the command line carries the
                // task-instance GUID ("taskhostw.exe {222A245B-...}")
                if (image == null || !image.EndsWith("taskhostw.exe", StringComparison.OrdinalIgnoreCase)) return;

                var info = new TaskLaunchInfo
                {
                    ProcessId = int.TryParse(processId, out int pid) ? pid : 0,
                    CommandLine = commandLine
                };

                var guidMatch = InstanceGuidRegex.Match(commandLine ?? string.Empty);
                if (guidMatch.Success)
                {
                    info.InstanceGuid = guidMatch.Groups[1].Value;
                    if (_byGuid.TryGetValue(info.InstanceGuid, out var known))
                    {
                        // Task Scheduler logged this instance first — keep its attribution
                        info.TaskPath = known.TaskPath;
                        info.UserContext = known.UserContext;
                        info.StartedAt = known.StartedAt;
                    }
                    else
                    {
                        _byGuid[info.InstanceGuid] = info;
                    }
                }

                if (info.ProcessId > 0) _byPid[info.ProcessId] = info;
            }
            catch { }
        }

        private void TaskScheduler_InstanceRecorded(object sender, EventRecordWrittenEventArgs e)
        {
            try
            {
                if (e.EventRecord == null) return;

                var doc = XDocument.Parse(e.EventRecord.ToXml());
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
                var eventData = doc.Root?.Element(ns + "EventData");
                if (eventData == null) return;

                string taskName = null, instanceId = null, userContext = null;
                foreach (var data in eventData.Elements(ns + "Data"))
                {
                    string name = data.Attribute("Name")?.Value;
                    if (name == "TaskName") taskName = data.Value;
                    else if (name == "InstanceId") instanceId = data.Value;
                    else if (name == "UserContext") userContext = data.Value;
                }
                if (string.IsNullOrEmpty(instanceId)) return;

                instanceId = instanceId.Trim('{', '}');
                var info = _byGuid.GetOrAdd(instanceId, _ => new TaskLaunchInfo { InstanceGuid = instanceId });

                if (!string.IsNullOrEmpty(taskName)) info.TaskPath = taskName;
                if (!string.IsNullOrEmpty(userContext)) info.UserContext = userContext;

                if (e.EventRecord.Id == 100) info.StartedAt ??= e.EventRecord.TimeCreated;
                else if (e.EventRecord.Id == 102) info.CompletedAt = e.EventRecord.TimeCreated;
            }
            catch { }
        }
    }
}

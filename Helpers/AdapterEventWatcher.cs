using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;

namespace AutoCommand.Helpers
{
    /// <summary>
    /// Subscribes to WMI network adapter creation/deletion events instead of polling.
    /// Fires callbacks when SSTP or Kernel Debug adapters appear or disappear.
    /// Zero CPU overhead when no adapters are changing.
    /// </summary>
    public sealed class AdapterEventWatcher : IDisposable
    {
        private ManagementEventWatcher _creationWatcher;
        private ManagementEventWatcher _deletionWatcher;

        /// <summary>Fired when a monitored adapter (SSTP or Kernel Debug) appears.</summary>
        public event Action<string> AdapterAppeared;

        /// <summary>Fired when a monitored adapter disappears (e.g. after removal).</summary>
        public event Action<string> AdapterRemoved;

        private bool _disposed;

        public void Start()
        {
            if (_disposed) return;
            Task.Run(() =>
            {
                try { StartInternal(); }
                catch (Exception ex) { Debug.WriteLine($"AdapterEventWatcher.Start error: {ex.Message}"); }
            });
        }

        private void StartInternal()
        {
            // WMI WITHIN 2: poll interval for WMI event delivery (in seconds).
            // This is handled inside the WMI service — NOT our process.
            const string CreationQuery =
                "SELECT * FROM __InstanceCreationEvent WITHIN 2 " +
                "WHERE TargetInstance ISA 'Win32_NetworkAdapter'";

            const string DeletionQuery =
                "SELECT * FROM __InstanceDeletionEvent WITHIN 2 " +
                "WHERE TargetInstance ISA 'Win32_NetworkAdapter'";

            _creationWatcher = new ManagementEventWatcher(
                new ManagementScope(@"\\.\root\cimv2"),
                new EventQuery(CreationQuery));
            _creationWatcher.EventArrived += OnAdapterCreated;
            _creationWatcher.Start();

            _deletionWatcher = new ManagementEventWatcher(
                new ManagementScope(@"\\.\root\cimv2"),
                new EventQuery(DeletionQuery));
            _deletionWatcher.EventArrived += OnAdapterDeleted;
            _deletionWatcher.Start();

            Debug.WriteLine("AdapterEventWatcher: Subscribed to WMI adapter creation/deletion events.");
        }

        private void OnAdapterCreated(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var adapter = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                string name = adapter?["Name"]?.ToString() ?? "";
                Debug.WriteLine($"AdapterEventWatcher: Adapter appeared — {name}");

                if (IsMonitored(name))
                    AdapterAppeared?.Invoke(name);
            }
            catch (Exception ex) { Debug.WriteLine($"AdapterEventWatcher creation handler error: {ex.Message}"); }
        }

        private void OnAdapterDeleted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var adapter = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                string name = adapter?["Name"]?.ToString() ?? "";
                Debug.WriteLine($"AdapterEventWatcher: Adapter removed — {name}");

                if (IsMonitored(name))
                    AdapterRemoved?.Invoke(name);
            }
            catch (Exception ex) { Debug.WriteLine($"AdapterEventWatcher deletion handler error: {ex.Message}"); }
        }

        private static bool IsMonitored(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("SSTP", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Kernel Debug", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("KDNIC", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _creationWatcher?.Stop(); _creationWatcher?.Dispose(); } catch { }
            try { _deletionWatcher?.Stop(); _deletionWatcher?.Dispose(); } catch { }
        }
    }
}

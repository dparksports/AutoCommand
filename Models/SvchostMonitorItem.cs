using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoCommand.Models
{
    public class SvchostMonitorItem : INotifyPropertyChanged
    {
        private long _rxPackets;
        private long _txPackets;
        private long _rxBytes;
        private long _txBytes;
        private DateTime _lastSeen;
        private string _processName;
        private string _hostname;

        public int ProcessId { get; set; }
        public string RemoteIp { get; set; }
        public string Protocol { get; set; }

        public string Hostname
        {
            get => _hostname;
            set
            {
                if (_hostname == value) return;
                _hostname = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsResolved));
                OnPropertyChanged(nameof(HostnameDisplay));
            }
        }

        /// <summary>True when the hostname has been resolved to a real name (not raw IP / empty).</summary>
        public bool IsResolved =>
            !string.IsNullOrEmpty(_hostname) &&
            _hostname != "Unknown" &&
            _hostname != "-" &&
            !System.Net.IPAddress.TryParse(_hostname, out _);

        /// <summary>Friendly display — shows italic '(resolving…)' when not yet resolved.</summary>
        public string HostnameDisplay =>
            IsResolved ? _hostname : string.IsNullOrEmpty(_hostname) ? "resolving…" : _hostname;

        public string ProcessName
        {
            get => _processName;
            set { _processName = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProcessDisplay)); }
        }

        /// <summary>Last time a Tx packet was seen for this remote IP.</summary>
        public DateTime LastSeen
        {
            get => _lastSeen;
            set { _lastSeen = value; OnPropertyChanged(); OnPropertyChanged(nameof(LastSeenDisplay)); }
        }

        /// <summary>Friendly display: "PID (name)" or just "PID" if name is unknown.</summary>
        public string ProcessDisplay => string.IsNullOrEmpty(ProcessName)
            ? ProcessId.ToString()
            : $"{ProcessId} ({ProcessName})";

        /// <summary>Human-readable last-seen time.</summary>
        public string LastSeenDisplay
        {
            get
            {
                if (_lastSeen == default) return "—";
                var age = DateTime.UtcNow - _lastSeen;
                if (age.TotalSeconds < 5)  return "just now";
                if (age.TotalSeconds < 60) return $"{(int)age.TotalSeconds}s ago";
                if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m ago";
                return _lastSeen.ToLocalTime().ToString("HH:mm:ss");
            }
        }

        public long RxPackets
        {
            get => _rxPackets;
            set { _rxPackets = value; OnPropertyChanged(); }
        }

        public long TxPackets
        {
            get => _txPackets;
            set { _txPackets = value; OnPropertyChanged(); }
        }

        public long RxBytes
        {
            get => _rxBytes;
            set { _rxBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(RxBytesDisplay)); }
        }

        public long TxBytes
        {
            get => _txBytes;
            set { _txBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(TxBytesDisplay)); }
        }

        public string RxBytesDisplay => FormatBytes(RxBytes);
        public string TxBytesDisplay => FormatBytes(TxBytes);

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Called by the UI refresh timer to re-evaluate the relative "Xs ago" text
        /// without needing to mutate LastSeen itself.
        /// </summary>
        public void RefreshLastSeenDisplay() =>
            OnPropertyChanged(nameof(LastSeenDisplay));

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1048576) return $"{(bytes / 1024.0):F1} KB";
            return $"{(bytes / 1048576.0):F1} MB";
        }
    }
}

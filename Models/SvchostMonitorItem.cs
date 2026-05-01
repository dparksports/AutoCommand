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

        public int ProcessId { get; set; }
        public string RemoteIp { get; set; }
        public string Hostname { get; set; }

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

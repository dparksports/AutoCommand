using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace AutoCommand.Helpers
{
    /// <summary>
    /// Native device removal using SetupAPI P/Invoke.
    /// Ported from DeviceMonitorCS — removes WAN Miniports, WiFi Direct, KDNET adapters.
    /// </summary>
    public static class WanMiniportRemover
    {
        private static readonly Guid NetworkClassGuid = new Guid("4d36e972-e325-11ce-bfc1-08002be10318");
        private const int DIF_REMOVE = 0x00000005;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, System.Text.StringBuilder DeviceInstanceId, int DeviceInstanceIdSize, out int RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiCallClassInstaller(int InstallFunction, IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        public static List<string> Execute() => ExecuteInternal(null);
        public static List<string> RemoveWifiDirect() => ExecuteInternal("MS_VWIFI");
        public static List<string> RemoveKdnet() => ExecuteInternal("KDNIC");

        private static List<string> ExecuteInternal(string specificPattern = null)
        {
            var results = new List<string>();
            Guid guid = NetworkClassGuid;

            IntPtr hDevInfo = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0);
            if (hDevInfo == IntPtr.Zero)
            {
                results.Add("Failed to get device information set.");
                return results;
            }

            try
            {
                SP_DEVINFO_DATA devInfo = new SP_DEVINFO_DATA();
                devInfo.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

                uint index = 0;
                while (SetupDiEnumDeviceInfo(hDevInfo, index, ref devInfo))
                {
                    bool removed = false;
                    string instanceId = GetDeviceInstanceId(hDevInfo, devInfo);

                    bool match = specificPattern != null
                        ? (instanceId != null && instanceId.Contains(specificPattern))
                        : IsWanMiniport(instanceId);

                    if (match)
                    {
                        try
                        {
                            if (SetupDiCallClassInstaller(DIF_REMOVE, hDevInfo, ref devInfo))
                            {
                                results.Add($"Removed: {instanceId}");
                                removed = true;
                            }
                            else
                            {
                                results.Add($"Failed to remove: {instanceId} (Error: {Marshal.GetLastWin32Error()})");
                            }
                        }
                        catch (Exception ex)
                        {
                            results.Add($"Exception removing {instanceId}: {ex.Message}");
                        }
                    }

                    if (!removed) index++;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }

            return results;
        }

        private static string GetDeviceInstanceId(IntPtr hDevInfo, SP_DEVINFO_DATA devInfo)
        {
            int requiredSize;
            SetupDiGetDeviceInstanceId(hDevInfo, ref devInfo, null, 0, out requiredSize);
            var sb = new System.Text.StringBuilder(requiredSize);
            SetupDiGetDeviceInstanceId(hDevInfo, ref devInfo, sb, requiredSize, out requiredSize);
            return sb.ToString().ToUpper();
        }

        private static bool IsWanMiniport(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return false;
            return instanceId.Contains("MS_L2TPMINIPORT") ||
                   instanceId.Contains("MS_PPTPMINIPORT") ||
                   instanceId.Contains("MS_SSTPMINIPORT") ||
                   instanceId.Contains("MS_IKEV2MINIPORT") ||
                   instanceId.Contains("MS_AGILEVPNMINIPORT") ||
                   instanceId.Contains("MS_NDISWANIP") ||
                   instanceId.Contains("MS_NDISWANIPV6") ||
                   instanceId.Contains("MS_PPPOEMINIPORT") ||
                   instanceId.Contains("MS_NDISWANBH") ||
                   instanceId.Contains("MS_VWIFI") ||
                   instanceId.Contains("KDNIC");
        }
    }
}


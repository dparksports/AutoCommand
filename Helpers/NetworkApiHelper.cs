using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using AutoCommand.Models;

namespace AutoCommand.Helpers
{
    public static class NetworkApiHelper
    {
        // IP Helper API constants
        private const int AF_INET = 2; // IPv4
        private const int TCP_TABLE_OWNER_PID_ALL = 5;
        private const int UDP_TABLE_OWNER_PID = 1;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tblClass, uint reserved = 0);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tblClass, uint reserved = 0);

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint remoteAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] remotePort;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint owningPid;
        }

        public static List<NetworkConnectionItem> GetActiveTcpConnections()
        {
            var connections = new List<NetworkConnectionItem>();
            int bufferSize = 0;

            GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL);
            IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);

            try
            {
                if (GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL) == 0)
                {
                    int rowCount = Marshal.ReadInt32(tcpTablePtr);
                    IntPtr rowPtr = tcpTablePtr + 4; // Move past dwNumEntries

                    for (int i = 0; i < rowCount; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                        
                        string procName = "Unknown";
                        try
                        {
                            var proc = Process.GetProcessById((int)row.owningPid);
                            procName = proc.ProcessName;
                        }
                        catch { }

                        connections.Add(new NetworkConnectionItem
                        {
                            Protocol = "TCP",
                            LocalAddress = $"{new IPAddress(row.localAddr)}:{BitConverter.ToUInt16(new[] { row.localPort[1], row.localPort[0] }, 0)}",
                            RemoteAddress = $"{new IPAddress(row.remoteAddr)}:{BitConverter.ToUInt16(new[] { row.remotePort[1], row.remotePort[0] }, 0)}",
                            State = FormatTcpState(row.state),
                            ProcessId = (int)row.owningPid,
                            ProcessName = procName
                        });

                        rowPtr += Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tcpTablePtr);
            }

            return connections;
        }

        public static List<NetworkConnectionItem> GetActiveUdpConnections()
        {
            var connections = new List<NetworkConnectionItem>();
            int bufferSize = 0;

            GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, UDP_TABLE_OWNER_PID);
            IntPtr udpTablePtr = Marshal.AllocHGlobal(bufferSize);

            try
            {
                if (GetExtendedUdpTable(udpTablePtr, ref bufferSize, true, AF_INET, UDP_TABLE_OWNER_PID) == 0)
                {
                    int rowCount = Marshal.ReadInt32(udpTablePtr);
                    IntPtr rowPtr = udpTablePtr + 4;

                    for (int i = 0; i < rowCount; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);

                        string procName = "Unknown";
                        try
                        {
                            var proc = Process.GetProcessById((int)row.owningPid);
                            procName = proc.ProcessName;
                        }
                        catch { }

                        connections.Add(new NetworkConnectionItem
                        {
                            Protocol = "UDP",
                            LocalAddress = $"{new IPAddress(row.localAddr)}:{BitConverter.ToUInt16(new[] { row.localPort[1], row.localPort[0] }, 0)}",
                            RemoteAddress = "*:*",
                            State = "",
                            ProcessId = (int)row.owningPid,
                            ProcessName = procName
                        });

                        rowPtr += Marshal.SizeOf(typeof(MIB_UDPROW_OWNER_PID));
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(udpTablePtr);
            }

            return connections;
        }

        private static string FormatTcpState(uint state)
        {
            return state switch
            {
                1 => "CLOSED",
                2 => "LISTEN",
                3 => "SYN_SENT",
                4 => "SYN_RCVD",
                5 => "ESTABLISHED",
                6 => "FIN_WAIT1",
                7 => "FIN_WAIT2",
                8 => "CLOSE_WAIT",
                9 => "CLOSING",
                10 => "LAST_ACK",
                11 => "TIME_WAIT",
                12 => "DELETE_TCB",
                _ => state.ToString()
            };
        }
    }
}

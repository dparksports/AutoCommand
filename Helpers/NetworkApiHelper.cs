using System;
using System.Collections.Concurrent;
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
        private const int AF_INET = 2;   // IPv4
        private const int AF_INET6 = 23; // IPv6
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
        public struct MIB_TCP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] localAddr;
            public uint localScopeId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] remoteAddr;
            public uint remoteScopeId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] remotePort;
            public uint state;
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

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_UDP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] localAddr;
            public uint localScopeId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint owningPid;
        }

        private static readonly ConcurrentDictionary<int, string> _processNameCache = new();

        public static List<NetworkConnectionItem> GetActiveTcpConnections()
        {
            var connections = new List<NetworkConnectionItem>();
            connections.AddRange(GetTcpConnections(AF_INET));
            connections.AddRange(GetTcpConnections(AF_INET6));
            return connections;
        }

        private static List<NetworkConnectionItem> GetTcpConnections(int ipVersion)
        {
            var connections = new List<NetworkConnectionItem>();
            int bufferSize = 0;

            GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, ipVersion, TCP_TABLE_OWNER_PID_ALL);
            IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);

            try
            {
                if (GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, ipVersion, TCP_TABLE_OWNER_PID_ALL) == 0)
                {
                    int rowCount = Marshal.ReadInt32(tcpTablePtr);
                    IntPtr rowPtr = tcpTablePtr + 4;

                    for (int i = 0; i < rowCount; i++)
                    {
                        if (ipVersion == AF_INET)
                        {
                            var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                            connections.Add(new NetworkConnectionItem
                            {
                                Protocol = "TCP",
                                LocalAddress = $"{new IPAddress(row.localAddr)}:{BitConverter.ToUInt16(new[] { row.localPort[1], row.localPort[0] }, 0)}",
                                RemoteAddress = $"{new IPAddress(row.remoteAddr)}:{BitConverter.ToUInt16(new[] { row.remotePort[1], row.remotePort[0] }, 0)}",
                                State = FormatTcpState(row.state),
                                ProcessId = (int)row.owningPid,
                                ProcessName = GetProcessName((int)row.owningPid)
                            });
                            rowPtr += Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                        }
                        else
                        {
                            var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(rowPtr);
                            connections.Add(new NetworkConnectionItem
                            {
                                Protocol = "TCPv6",
                                LocalAddress = $"[{new IPAddress(row.localAddr)}]:{BitConverter.ToUInt16(new[] { row.localPort[1], row.localPort[0] }, 0)}",
                                RemoteAddress = $"[{new IPAddress(row.remoteAddr)}]:{BitConverter.ToUInt16(new[] { row.remotePort[1], row.remotePort[0] }, 0)}",
                                State = FormatTcpState(row.state),
                                ProcessId = (int)row.owningPid,
                                ProcessName = GetProcessName((int)row.owningPid)
                            });
                            rowPtr += Marshal.SizeOf(typeof(MIB_TCP6ROW_OWNER_PID));
                        }
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
            connections.AddRange(GetUdpConnections(AF_INET));
            connections.AddRange(GetUdpConnections(AF_INET6));
            return connections;
        }

        private static List<NetworkConnectionItem> GetUdpConnections(int ipVersion)
        {
            var connections = new List<NetworkConnectionItem>();
            int bufferSize = 0;

            GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, ipVersion, UDP_TABLE_OWNER_PID);
            IntPtr udpTablePtr = Marshal.AllocHGlobal(bufferSize);

            try
            {
                if (GetExtendedUdpTable(udpTablePtr, ref bufferSize, true, ipVersion, UDP_TABLE_OWNER_PID) == 0)
                {
                    int rowCount = Marshal.ReadInt32(udpTablePtr);
                    IntPtr rowPtr = udpTablePtr + 4;

                    for (int i = 0; i < rowCount; i++)
                    {
                        if (ipVersion == AF_INET)
                        {
                            var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                            connections.Add(new NetworkConnectionItem
                            {
                                Protocol = "UDP",
                                LocalAddress = $"{new IPAddress(row.localAddr)}:{BitConverter.ToUInt16(new[] { row.localPort[1], row.localPort[0] }, 0)}",
                                RemoteAddress = "*:*",
                                State = "",
                                ProcessId = (int)row.owningPid,
                                ProcessName = GetProcessName((int)row.owningPid)
                            });
                            rowPtr += Marshal.SizeOf(typeof(MIB_UDPROW_OWNER_PID));
                        }
                        else
                        {
                            var row = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(rowPtr);
                            connections.Add(new NetworkConnectionItem
                            {
                                Protocol = "UDPv6",
                                LocalAddress = $"[{new IPAddress(row.localAddr)}]:{BitConverter.ToUInt16(new[] { row.localPort[1], row.localPort[0] }, 0)}",
                                RemoteAddress = "*:*",
                                State = "",
                                ProcessId = (int)row.owningPid,
                                ProcessName = GetProcessName((int)row.owningPid)
                            });
                            rowPtr += Marshal.SizeOf(typeof(MIB_UDP6ROW_OWNER_PID));
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(udpTablePtr);
            }

            return connections;
        }

        private static string GetProcessName(int pid)
        {
            if (pid == 0) return "Idle";
            if (pid == 4) return "System";

            if (_processNameCache.TryGetValue(pid, out string cachedName))
            {
                return cachedName;
            }

            try
            {
                var proc = Process.GetProcessById(pid);
                string name = proc.ProcessName;
                _processNameCache[pid] = name;
                return name;
            }
            catch
            {
                return "Unknown";
            }
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

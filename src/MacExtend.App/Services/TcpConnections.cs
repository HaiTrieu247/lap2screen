using System;
using System.Runtime.InteropServices;
using MacExtend.Native;

namespace MacExtend.Services
{
    /// <summary>Counts established TCP connections owned by a process (IPv4 and IPv6, loopback excluded).</summary>
    internal static class TcpConnections
    {
        // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, owningPid
        private const int Row4Size = 24, Row4State = 0, Row4Remote = 12, Row4Pid = 20;
        // MIB_TCP6ROW_OWNER_PID: localAddr[16], localScope, localPort, remoteAddr[16], remoteScope, remotePort, state, owningPid
        private const int Row6Size = 56, Row6Remote = 24, Row6State = 48, Row6Pid = 52;

        public static int CountEstablished(int pid)
        {
            if (pid <= 0) return 0;
            return Count(Win32.AF_INET, pid) + Count(Win32.AF_INET6, pid);
        }

        private static int Count(int family, int pid)
        {
            var size = 0;
            Win32.GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, Win32.TCP_TABLE_OWNER_PID_ALL, 0);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    var rc = Win32.GetExtendedTcpTable(buffer, ref size, false, family, Win32.TCP_TABLE_OWNER_PID_ALL, 0);
                    if (rc == Win32.ERROR_INSUFFICIENT_BUFFER) continue; // table grew between calls
                    if (rc != 0) return 0;
                    return family == Win32.AF_INET ? CountRows4(buffer, pid) : CountRows6(buffer, pid);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            return 0;
        }

        private static int CountRows4(IntPtr table, int pid)
        {
            var n = Marshal.ReadInt32(table);
            var count = 0;
            for (var i = 0; i < n; i++)
            {
                var row = table + 4 + i * Row4Size;
                if (Marshal.ReadInt32(row, Row4Pid) != pid || Marshal.ReadInt32(row, Row4State) != Win32.MIB_TCP_STATE_ESTAB) continue;
                if (Marshal.ReadByte(row, Row4Remote) == 127) continue;
                count++;
            }
            return count;
        }

        private static int CountRows6(IntPtr table, int pid)
        {
            var n = Marshal.ReadInt32(table);
            var count = 0;
            for (var i = 0; i < n; i++)
            {
                var row = table + 4 + i * Row6Size;
                if (Marshal.ReadInt32(row, Row6Pid) != pid || Marshal.ReadInt32(row, Row6State) != Win32.MIB_TCP_STATE_ESTAB) continue;
                if (IsLoopback6(row + Row6Remote)) continue;
                count++;
            }
            return count;
        }

        private static bool IsLoopback6(IntPtr address)
        {
            for (var i = 0; i < 15; i++)
            {
                if (Marshal.ReadByte(address, i) != 0) return false;
            }
            return Marshal.ReadByte(address, 15) == 1;
        }
    }
}

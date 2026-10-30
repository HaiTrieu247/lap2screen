using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MacExtend.Native
{
    /// <summary>Whether this process runs elevated, and whether its account can elevate by itself (an administrator behind UAC).</summary>
    internal static class Elevation
    {
        private const int TokenElevationType = 18;
        private const int TokenElevationTypeLimited = 3;

        public static bool IsElevated
        {
            get
            {
                using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>
        /// Elevated now, or a split UAC token that elevates without anyone else's password. A standard account is not:
        /// a task "with highest privileges" would run it without administrator rights, so Wi-Fi asks UAC per session.
        /// </summary>
        public static bool IsAdminAccount
        {
            get
            {
                if (IsElevated) return true;
                using (var id = WindowsIdentity.GetCurrent())
                {
                    var buffer = Marshal.AllocHGlobal(sizeof(int));
                    try
                    {
                        return GetTokenInformation(id.Token, TokenElevationType, buffer, sizeof(int), out _)
                               && Marshal.ReadInt32(buffer) == TokenElevationTypeLimited;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
        }

        /// <summary>
        /// DOMAIN\name of this process's account. Read by the unelevated app and handed to the elevated setup, so the
        /// Scheduled Task's principal is the logged-on user, not the administrator who approved UAC.
        /// </summary>
        public static string CurrentUserName()
        {
            using (var id = WindowsIdentity.GetCurrent()) return id.Name;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returned);
    }
}

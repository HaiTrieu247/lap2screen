using System;
using System.Globalization;

namespace MacExtend.CoreDeviceHost
{
    /// <summary>
    /// The "signal-ctrl-c PID" helper mode of whatever exe hosts serve-web (the probe, MacExtend.exe): attaches to the
    /// console of PID and sends exactly one CTRL_C_EVENT to every process on it, ignoring it itself. A separate
    /// process, so the host never leaves its own console. The exit code says whether the event was certainly sent,
    /// certainly not sent, or unknown (CoreDeviceProcessHost.Helper*).
    /// </summary>
    public static class CtrlCHelper
    {
        public const string Command = "signal-ctrl-c";

        /// <returns>true when <paramref name="args"/> asked for the helper; the process should exit with <paramref name="exitCode"/>.</returns>
        public static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = 0;
            if (args == null || args.Length == 0 || args[0] != Command) return false;
            if (args.Length != 2 || !uint.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid == 0)
            {
                exitCode = CoreDeviceProcessHost.HelperBadArguments;
                return true;
            }
            NativeMethods.FreeConsole();
            if (!NativeMethods.AttachConsole(pid))
            {
                exitCode = CoreDeviceProcessHost.HelperAttachFailed;
                return true;
            }
            NativeMethods.SetConsoleCtrlHandler(IntPtr.Zero, true);
            var sent = NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CTRL_C_EVENT, 0);
            exitCode = sent ? CoreDeviceProcessHost.HelperSent : CoreDeviceProcessHost.HelperGenerateFailed;
            return true;
        }

        /// <summary>
        /// "Ignore Ctrl+C" is inherited from whoever started this process (a CREATE_NEW_PROCESS_GROUP parent sets it)
        /// and would pass on to serve-web, which then never sees the one Ctrl+C that runs its cleanup.
        /// </summary>
        public static void ClearInheritedCtrlCIgnore() => NativeMethods.SetConsoleCtrlHandler(IntPtr.Zero, false);
    }
}

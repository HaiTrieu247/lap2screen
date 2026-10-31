using System.Globalization;
using System.Text.RegularExpressions;

namespace MacExtend.Core.CoreDevice
{
    public struct SetupQuery
    {
        public int Rules;
        public bool Task;

        /// <summary>
        /// Every rule AddFirewallRules makes (python.exe and pythonw.exe), and the task when this machine starts tunneld
        /// with it. Two rules are an older setup without pythonw.exe's.
        /// </summary>
        public bool IsComplete(bool needsTask) => Rules >= CoreDeviceSetupScripts.FirewallRuleCount && (Task || !needsTask);
    }

    /// <summary>
    /// The PowerShell that sets Windows up for Wi-Fi control (run elevated by MacExtend.exe --coredevice-setup) and
    /// undoes it (--coredevice-remove, the uninstaller). Every value is a single-quoted literal (PowerShellText).
    /// </summary>
    public static class CoreDeviceSetupScripts
    {
        public const string TaskPath = @"\MacExtend\";
        public const string TaskName = "MacExtend CoreDevice tunnel";
        public const string TaskFullName = TaskPath + TaskName;
        public const string FirewallGroup = "MacExtend CoreDevice";
        /// <summary>The rules AddFirewallRules creates: tunnel video and Wi-Fi discovery, for python.exe and pythonw.exe.</summary>
        public const int FirewallRuleCount = 4;

        private const string Stop = "$ErrorActionPreference = 'Stop'";

        /// <summary>
        /// The on-demand task that starts tunneld elevated without a UAC prompt: the user's own account, highest
        /// privileges, only while that user is logged on, no trigger. The action is the runtime's pythonw.exe with a
        /// fixed bootstrap, never MacExtend.exe: the task runs with the user's own (writable) environment, and an
        /// elevated .NET process would honour COR_PROFILER_PATH. The executable is double-quoted because Task Scheduler
        /// joins Command and Arguments without adding quotes.
        /// </summary>
        public static string RegisterTask(string pythonwPath, string workingDirectory, string userName)
        {
            // Task Scheduler expands %VAR% in the command and arguments at every start, from the task user's own
            // (unelevated, writable) environment, so a '%' would let the action change after it was created.
            RejectPercent(pythonwPath, nameof(pythonwPath));
            RejectPercent(workingDirectory, nameof(workingDirectory));
            RejectPercent(userName, nameof(userName));
            var argument = WindowsCommandLine.Join(CoreDeviceArguments.TunneldBootstrap());
            RejectPercent(argument, "argument");
            return string.Join("\n",
                Stop,
                "$action = New-ScheduledTaskAction -Execute " + PowerShellText.Literal(WindowsCommandLine.Quote(pythonwPath))
                    + " -Argument " + PowerShellText.Literal(argument)
                    + " -WorkingDirectory " + PowerShellText.Literal(workingDirectory),
                "$principal = New-ScheduledTaskPrincipal -UserId " + PowerShellText.Literal(userName) + " -LogonType Interactive -RunLevel Highest",
                "$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew",
                "Register-ScheduledTask -TaskPath " + PowerShellText.Literal(TaskPath) + " -TaskName " + PowerShellText.Literal(TaskName)
                    + " -Action $action -Principal $principal -Settings $settings -Force | Out-Null");
        }

        private static void RejectPercent(string value, string parameter)
        {
            if (value != null && value.IndexOf('%') >= 0)
                throw new System.ArgumentException("The value goes into a scheduled task, where '%' is expanded as an environment variable.", parameter);
        }

        /// <summary>
        /// Ends tunneld if the task is running it (its pythonw.exe holds the runtime's files); does nothing when the task
        /// is missing or idle. Run before the runtime folder is replaced or removed.
        /// </summary>
        public static string StopTask() => IfFound(FindTask(), "Stop-ScheduledTask");

        public static string RemoveTask() => IfFound(FindTask(), "Unregister-ScheduledTask -Confirm:$false");

        private static string FindTask() =>
            "Get-ScheduledTask -TaskPath " + PowerShellText.Literal(TaskPath) + " -TaskName " + PowerShellText.Literal(TaskName);

        /// <summary>
        /// Runs <paramref name="action"/> on what <paramref name="lookup"/> finds, and nothing when it finds nothing. A lookup
        /// that finds nothing writes an error even with SilentlyContinue, and powershell.exe -Command exits 1 when its last
        /// command wrote one, so the script ends with an explicit exit 0; a failing action stops it, which exits 1.
        /// </summary>
        private static string IfFound(string lookup, string action) => string.Join("\n",
            "$found = @(" + lookup + " -ErrorAction SilentlyContinue)",
            "if ($found.Count -gt 0) { $found | " + action + " -ErrorAction Stop }",
            "exit 0");

        /// <summary>
        /// Video arrives as unsolicited UDP on the tunnel adapter (Public, a new network for every tunnel), and Bonjour
        /// answers on private Wi-Fi; both only for the runtime's python.exe (serve-web) and pythonw.exe (tunneld). Having
        /// rules for those programs also keeps Windows from asking, and from adding its own Block rules for Public.
        /// </summary>
        public static string AddFirewallRules(string pythonDirectory)
        {
            if (pythonDirectory == null) throw new System.ArgumentNullException(nameof(pythonDirectory));
            if (pythonDirectory.TrimEnd('\\').EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase))
                throw new System.ArgumentException("Pass the python folder, not a program.", nameof(pythonDirectory));
            var group = PowerShellText.Literal(FirewallGroup);
            var lines = new System.Collections.Generic.List<string>
            {
                Stop,
                "Get-NetFirewallRule -Group " + group + " -ErrorAction SilentlyContinue | Remove-NetFirewallRule",
            };
            foreach (var exe in new[] { "python.exe", "pythonw.exe" })
            {
                var program = PowerShellText.Literal(pythonDirectory.TrimEnd('\\') + "\\" + exe);
                lines.Add("New-NetFirewallRule -Group " + group + " -DisplayName 'MacExtend CoreDevice video over tunnel (" + exe + ")' -Direction Inbound -Action Allow -Protocol UDP -RemoteAddress 'fc00::/7' -Program " + program + " -Profile Any | Out-Null");
                lines.Add("New-NetFirewallRule -Group " + group + " -DisplayName 'MacExtend CoreDevice Wi-Fi discovery (" + exe + ")' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 5353 -Program " + program + " -Profile Private | Out-Null");
            }
            return string.Join("\n", lines);
        }

        public static string RemoveFirewallRules() =>
            IfFound("Get-NetFirewallRule -Group " + PowerShellText.Literal(FirewallGroup), "Remove-NetFirewallRule");

        /// <summary>Unelevated check; prints "rules=N task=0|1".</summary>
        public static string Query() => string.Join("\n",
            "$rules = @(Get-NetFirewallRule -Group " + PowerShellText.Literal(FirewallGroup) + " -ErrorAction SilentlyContinue).Count",
            "$task = if (Get-ScheduledTask -TaskPath " + PowerShellText.Literal(TaskPath) + " -TaskName " + PowerShellText.Literal(TaskName) + " -ErrorAction SilentlyContinue) { 1 } else { 0 }",
            "\"rules=$rules task=$task\"");

        public static SetupQuery ParseQuery(string output)
        {
            var m = Regex.Match(output ?? "", @"rules=(\d+) task=([01])", RegexOptions.CultureInvariant);
            if (!m.Success) return new SetupQuery();
            return new SetupQuery
            {
                Rules = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Task = m.Groups[2].Value == "1",
            };
        }
    }
}

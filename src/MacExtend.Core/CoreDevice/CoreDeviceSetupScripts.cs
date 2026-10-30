using System.Globalization;
using System.Text.RegularExpressions;

namespace MacExtend.Core.CoreDevice
{
    public struct SetupQuery
    {
        public int Rules;
        public bool Task;
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
        public const string TunneldArg = "--coredevice-tunneld";

        private const string Stop = "$ErrorActionPreference = 'Stop'";

        /// <summary>
        /// The on-demand task that starts tunneld elevated without a UAC prompt: the user's own account, highest
        /// privileges, only while that user is logged on, no trigger. Its command line is fixed here, so starting it
        /// (all a normal user can do) never runs anything else.
        /// </summary>
        public static string RegisterTask(string exePath, string userName, string userProfile)
        {
            var argument = WindowsCommandLine.Join(new[] { TunneldArg, userProfile });
            return string.Join("\n",
                Stop,
                "$action = New-ScheduledTaskAction -Execute " + PowerShellText.Literal(exePath) + " -Argument " + PowerShellText.Literal(argument),
                "$principal = New-ScheduledTaskPrincipal -UserId " + PowerShellText.Literal(userName) + " -LogonType Interactive -RunLevel Highest",
                "$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew",
                "Register-ScheduledTask -TaskPath " + PowerShellText.Literal(TaskPath) + " -TaskName " + PowerShellText.Literal(TaskName)
                    + " -Action $action -Principal $principal -Settings $settings -Force | Out-Null");
        }

        public static string RemoveTask() =>
            "Get-ScheduledTask -TaskPath " + PowerShellText.Literal(TaskPath) + " -TaskName " + PowerShellText.Literal(TaskName)
            + " -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false";

        /// <summary>
        /// Video arrives as unsolicited UDP on the tunnel adapter (Public, a new network for every tunnel), and Bonjour
        /// answers on private Wi-Fi; both only for the runtime's python.exe. Having rules for that program also keeps
        /// Windows from asking, and from adding its own Block rules for Public (what run 1 ran into).
        /// </summary>
        public static string AddFirewallRules(string pythonPath)
        {
            var group = PowerShellText.Literal(FirewallGroup);
            var program = PowerShellText.Literal(pythonPath);
            return string.Join("\n",
                Stop,
                "Get-NetFirewallRule -Group " + group + " -ErrorAction SilentlyContinue | Remove-NetFirewallRule",
                "New-NetFirewallRule -Group " + group + " -DisplayName 'MacExtend CoreDevice video over tunnel' -Direction Inbound -Action Allow -Protocol UDP -RemoteAddress 'fc00::/7' -Program " + program + " -Profile Any | Out-Null",
                "New-NetFirewallRule -Group " + group + " -DisplayName 'MacExtend CoreDevice Wi-Fi discovery' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 5353 -Program " + program + " -Profile Private | Out-Null");
        }

        public static string RemoveFirewallRules() =>
            "Get-NetFirewallRule -Group " + PowerShellText.Literal(FirewallGroup) + " -ErrorAction SilentlyContinue | Remove-NetFirewallRule";

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

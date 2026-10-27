using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using MacExtend.Core;

namespace MacExtend.Services
{
    /// <summary>Inbound firewall rules for uxplay.exe. Without an allow rule the Mac never sees this receiver.</summary>
    internal static class Firewall
    {
        public const string RuleName = "MacExtend (uxplay)";

        public static async Task<FirewallStatus> GetStatusAsync()
        {
            var script = "$p = " + PowerShellRunner.Literal(AppPaths.UxPlayExe) + @"
$rules = @(Get-NetFirewallApplicationFilter -Program $p -ErrorAction SilentlyContinue |
           Get-NetFirewallRule -ErrorAction SilentlyContinue |
           Where-Object { $_.Enabled -eq 'True' -and $_.Direction -eq 'Inbound' })
'ALLOW=' + @($rules | Where-Object { $_.Action -eq 'Allow' }).Count
'BLOCK=' + @($rules | Where-Object { $_.Action -eq 'Block' }).Count";
            var result = await PowerShellRunner.RunAsync(script);
            return FirewallStatusParser.Parse(result.Output);
        }

        /// <summary>
        /// Removes block rules Windows created when its first-run prompt was dismissed, then adds
        /// MacExtend's allow rule for private and domain networks.
        /// </summary>
        public static Task<ScriptResult> FixAsync()
        {
            var script = "$p = " + PowerShellRunner.Literal(AppPaths.UxPlayExe) + @"
Get-NetFirewallApplicationFilter -Program $p -ErrorAction SilentlyContinue |
    Get-NetFirewallRule -ErrorAction SilentlyContinue |
    Where-Object { $_.Action -eq 'Block' } | Remove-NetFirewallRule
Get-NetFirewallRule -DisplayName " + PowerShellRunner.Literal(RuleName) + @" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName " + PowerShellRunner.Literal(RuleName) + @" -Direction Inbound -Program $p -Action Allow -Profile Private,Domain | Out-Null";
            return PowerShellRunner.RunElevatedAsync(script);
        }
    }

    /// <summary>Windows network category. On "Public" networks Windows hides this PC from other devices.</summary>
    internal static class NetworkProfile
    {
        public static async Task<IReadOnlyList<NetworkConnection>> GetAsync()
        {
            const string script =
                "Get-NetConnectionProfile -ErrorAction SilentlyContinue | ForEach-Object { " +
                "'{0}|{1}|{2}' -f $_.InterfaceIndex, ($_.InterfaceAlias -replace '\\|', '/'), $_.NetworkCategory }";
            var result = await PowerShellRunner.RunAsync(script);
            return NetworkProfileParser.Parse(result.Output);
        }

        public static Task<ScriptResult> MakePrivateAsync(int interfaceIndex) =>
            PowerShellRunner.RunElevatedAsync(
                "Set-NetConnectionProfile -InterfaceIndex " + interfaceIndex.ToString(CultureInfo.InvariantCulture) +
                " -NetworkCategory Private");
    }
}

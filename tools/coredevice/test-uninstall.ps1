<#
.SYNOPSIS
    CI (elevated windows-latest runner): the real installer, a real CoreDevice setup in the installed copy, then the
    real uninstaller: it must take the Scheduled Task, the firewall rules and the whole coredevice folder away.

.PARAMETER Setup
    The installer built in the same job (dist/MacExtend-<label>-setup.exe).

.PARAMETER Zip
    The coredevice runtime zip of the same build.
#>
param(
    [Parameter(Mandatory = $true)] [string]$Setup,
    [Parameter(Mandatory = $true)] [string]$Zip
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'this test runs on Windows only' }
$Setup = (Resolve-Path -LiteralPath $Setup).Path
$Zip = (Resolve-Path -LiteralPath $Zip).Path
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$app = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'MacExtend'
$group = 'MacExtend CoreDevice'
$taskPath = '\MacExtend\'
$taskName = 'MacExtend CoreDevice tunnel'

function Get-Task { Get-ScheduledTask -TaskPath $taskPath -TaskName $taskName -ErrorAction SilentlyContinue }
function Get-RuleCount { @(Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue).Count }

function Invoke-Exe([string]$Exe, [string[]]$Arguments, [int]$TimeoutSeconds = 600) {
    $p = Start-Process -FilePath $Exe -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    $null = $p.Handle
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) { $p.Kill(); throw "$(Split-Path $Exe -Leaf) did not finish within $TimeoutSeconds s" }
    $p.WaitForExit()
    $p.ExitCode
}

try {
    if (Test-Path -LiteralPath $app) { throw "$app exists already" }

    Write-Host '== install silently'
    $code = Invoke-Exe $Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$app`"")
    if ($code -ne 0) { throw "installer exit code $code" }
    foreach ($f in @('MacExtend.exe', 'unins000.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $app $f))) { throw "$f missing after the install" }
    }

    Write-Host '== setup in the installed copy'
    $code = Invoke-Exe (Join-Path $app 'MacExtend.exe') @('--coredevice-setup', "`"$Zip`"", "`"$user`"", '1') 300
    if ($code -ne 0) { throw "--coredevice-setup exit code $code" }
    if (-not (Test-Path -LiteralPath (Join-Path $app 'coredevice\python\pythonw.exe'))) { throw 'runtime not installed' }
    if ((Get-RuleCount) -ne 4) { throw "expected 4 firewall rules, found $(Get-RuleCount)" }
    if (-not (Get-Task)) { throw 'task missing' }
    # what the in-app updater leaves behind when it is interrupted: not in the installer's own file list
    foreach ($leftover in @('coredevice.old', 'coredevice.new')) {
        New-Item -ItemType Directory -Force (Join-Path $app "$leftover\python") | Out-Null
        Set-Content -LiteralPath (Join-Path $app "$leftover\python\python.exe") -Value 'x'
    }

    Write-Host '== uninstall silently'
    # unins000.exe starts a copy of itself from the temp folder and exits at once, so wait for the files to go
    $null = Invoke-Exe (Join-Path $app 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') 120
    $deadline = [DateTime]::UtcNow.AddSeconds(240)
    while ((Test-Path -LiteralPath (Join-Path $app 'unins000.exe')) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Seconds 2 }
    if (Test-Path -LiteralPath (Join-Path $app 'unins000.exe')) { throw 'the uninstaller did not finish within 240 s' }

    foreach ($gone in @('coredevice', 'coredevice.old', 'coredevice.new', 'MacExtend.exe')) {
        if (Test-Path -LiteralPath (Join-Path $app $gone)) { throw "$gone is still in $app after the uninstall" }
    }
    if (Get-Task) { throw 'the task survived the uninstall' }
    if ((Get-RuleCount) -ne 0) { throw 'the firewall rules survived the uninstall' }
    Write-Host 'coredevice uninstall ok'
}
finally {
    $unins = Join-Path $app 'unins000.exe'
    if (Test-Path -LiteralPath $unins) {
        # a failed run: take the install away again so the runner is left as it was
        try { $null = Invoke-Exe (Join-Path $app 'MacExtend.exe') @('--coredevice-remove') 120 } catch { }
        Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        Remove-Item -LiteralPath $app -Recurse -Force -ErrorAction SilentlyContinue
    }
}

<#
.SYNOPSIS
    Smoke-tests MacExtend.CoreDeviceProbe.exe against a fake serve-web, without a device.

.DESCRIPTION
    Runs tests\console_driver.py with the venv's interpreter (set up by setup.ps1, so the probe's version check
    and psutil are real). Every scenario starts the probe in a hidden console of its own; Ctrl+C is only ever
    sent to that console. Exits non-zero if any scenario fails.

.PARAMETER Probe
    Path to the built MacExtend.CoreDeviceProbe.exe.

.PARAMETER Python
    The venv interpreter; defaults to tools\coredevice\.venv\Scripts\python.exe.

.PARAMETER Only
    Scenario names to run (see SCENARIOS in tests\console_driver.py); default all.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Probe,
    [string]$Python = (Join-Path $PSScriptRoot '.venv\Scripts\python.exe'),
    [string[]]$Only = @(),
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'the probe smoke tests run on Windows only' }
$Probe = (Resolve-Path -LiteralPath $Probe).Path
$Python = (Resolve-Path -LiteralPath $Python).Path

$driverArgs = @('-I', (Join-Path $PSScriptRoot 'tests\console_driver.py'),
    '--probe', $Probe, '--python', $Python, '--fake', (Join-Path $PSScriptRoot 'tests\fake_serve_web.py'))
if ($Only.Count -gt 0) { $driverArgs += @('--only') + $Only }
if ($Keep) { $driverArgs += '--keep' }

& $Python @driverArgs
exit $LASTEXITCODE

<#
.SYNOPSIS
    Spike for "Điều khiển trực tiếp": can a normal (unelevated) PowerShell start a task registered elevated to run
    with highest privileges, and can it see the task and its firewall rules?

.DESCRIPTION
    1. Administrator PowerShell:  .\tools\coredevice\spike-task.ps1 -Create -User "$env:USERDOMAIN\$env:USERNAME"
    2. Normal PowerShell:         .\tools\coredevice\spike-task.ps1 -Run
    3. Administrator PowerShell:  .\tools\coredevice\spike-task.ps1 -Remove
#>
param([switch]$Create, [switch]$Run, [switch]$Remove, [string]$User)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$TaskPath = '\MacExtendSpike\'
$TaskName = 'Spike'
$Group = 'MacExtend Spike'
# C:\Users\Public: written by the elevated task, read by the normal shell
$Out = Join-Path $env:PUBLIC 'macextend-task-spike.txt'

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if ($Create) {
    if (-not (Test-Admin)) { throw 'run -Create from an administrator PowerShell' }
    if (-not $User) { throw '-User DOMAIN\name is required (the account that will run -Run)' }
    # S-1-16-12288 is the High integrity label: present only when the task really runs elevated
    $cmd = "whoami /groups > `"$Out`""
    $action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\cmd.exe" -Argument "/c $cmd"
    $principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
    New-NetFirewallRule -Group $Group -DisplayName 'MacExtend spike rule' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 5353 -Program "$env:SystemRoot\System32\cmd.exe" -Profile Private | Out-Null
    Remove-Item -LiteralPath $Out -ErrorAction SilentlyContinue
    'created; now run .\tools\coredevice\spike-task.ps1 -Run from a normal PowerShell'
}
elseif ($Run) {
    if (Test-Admin) { throw 'run -Run from a normal PowerShell, not as administrator' }
    $seen = [bool](Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue)
    & schtasks.exe /Query /TN "$TaskPath$TaskName" | Out-Null
    $query = $LASTEXITCODE
    $rules = @(Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue).Count
    & schtasks.exe /Run /TN "$TaskPath$TaskName"
    $run = $LASTEXITCODE
    Start-Sleep -Seconds 3
    $text = if (Test-Path -LiteralPath $Out) { Get-Content -LiteralPath $Out -Raw } else { '' }
    $elevated = $text -match 'S-1-16-12288'
    "Get-ScheduledTask sees the task: $seen"
    "schtasks /Query exit code:      $query"
    "firewall rules visible:         $rules"
    "schtasks /Run exit code:        $run"
    "task ran elevated:              $elevated"
    if ($run -eq 0 -and $elevated) { 'PASS' } else { 'FAIL' }
}
elseif ($Remove) {
    if (-not (Test-Admin)) { throw 'run -Remove from an administrator PowerShell' }
    Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false
    Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Remove-Item -LiteralPath $Out -ErrorAction SilentlyContinue
    'removed'
}
else {
    throw 'pass -Create, -Run or -Remove'
}

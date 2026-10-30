<#
.SYNOPSIS
    Creates or refreshes tools\coredevice\.venv with exactly the locked CoreDevice probe runtime.

.DESCRIPTION
    Installs requirements-win-x64-py313.lock.txt (pymobiledevice3 11.47.0 and every dependency, each pinned with
    hashes, wheels only) into a venv next to this script, then checks it: pip check, the installed version
    and serve-web --help, run the way the probe runs it. Running it again reuses the same venv and installs
    nothing beyond the lock. The Python given is only used to create the venv; it is never modified.

.PARAMETER Python
    Absolute path to a Python 3.13 x64 interpreter, for example
    C:\Users\me\AppData\Local\Programs\Python\Python313\python.exe

.EXAMPLE
    .\tools\coredevice\setup.ps1 -Python "$env:LOCALAPPDATA\Programs\Python\Python313\python.exe"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Python
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RequiredVersion = '11.47.0'
$Lock = Join-Path $PSScriptRoot 'requirements-win-x64-py313.lock.txt'
$Venv = Join-Path $PSScriptRoot '.venv'
$VenvPython = Join-Path $Venv 'Scripts\python.exe'

function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($Exe)) $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

function Assert-Python313x64 {
    param([string]$Exe, [string]$What)
    $info = & $Exe -I -c "import sys, struct; print('%d.%d %d %s' % (sys.version_info[0], sys.version_info[1], struct.calcsize('P') * 8, sys.platform))"
    if ($LASTEXITCODE -ne 0) { throw "$What could not be run: $Exe" }
    $parts = "$info".Trim() -split ' '
    if ($parts[0] -ne '3.13' -or $parts[1] -ne '64' -or $parts[2] -ne 'win32') {
        throw "$What must be CPython 3.13 x64 on Windows; $Exe reports '$("$info".Trim())'"
    }
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'setup.ps1 sets up the Windows runtime; run it on Windows' }
if (-not [IO.Path]::IsPathRooted($Python) -or -not (Test-Path -LiteralPath $Python -PathType Leaf)) {
    throw "-Python must be the absolute path of an existing python.exe (got '$Python')"
}

Assert-Python313x64 -Exe $Python -What 'the base interpreter'

if (Test-Path -LiteralPath $VenvPython -PathType Leaf) {
    Write-Host "reusing $Venv"
}
else {
    Write-Host "creating $Venv"
    Invoke-Native $Python @('-I', '-m', 'venv', $Venv)
}
Assert-Python313x64 -Exe $VenvPython -What 'the venv interpreter'

# --require-hashes: every file must match the lock; --no-deps: nothing is resolved beyond the lock;
# --only-binary: no package is built from source on this machine
Invoke-Native $VenvPython @('-I', '-m', 'pip', 'install', '--disable-pip-version-check', '--no-input',
    '--require-hashes', '--no-deps', '--only-binary', ':all:', '-r', $Lock)
Invoke-Native $VenvPython @('-I', '-m', 'pip', 'check', '--disable-pip-version-check')

$version = & $VenvPython -I -X utf8 -c "import importlib.metadata as m; print(m.version('pymobiledevice3'))"
if ($LASTEXITCODE -ne 0) { throw 'pymobiledevice3 is not importable in the venv' }
if ("$version".Trim() -ne $RequiredVersion) { throw "the venv has pymobiledevice3 $("$version".Trim()); the probe needs $RequiredVersion" }

# the same interpreter flags the probe uses (CoreDeviceArguments.ServeWeb)
$env:NO_COLOR = '1'
# stdout only: Windows PowerShell 5.1 turns redirected stderr lines into terminating errors under 'Stop'
$help = & $VenvPython -I -X utf8 -u -m pymobiledevice3 developer core-device display serve-web --help | Out-String
if ($LASTEXITCODE -ne 0) { throw "serve-web --help failed:`n$help" }
foreach ($option in @('--bind', '--http-port', '--no-audio', '--userspace', '--tunnel')) {
    if ($help -notmatch [regex]::Escape($option)) { throw "serve-web --help does not list $option; the probe's command line no longer matches pymobiledevice3" }
}

Write-Host "pymobiledevice3 $RequiredVersion ready: $VenvPython"

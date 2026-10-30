<#
.SYNOPSIS
    Builds the relocatable CoreDevice runtime: CPython 3.13 x64 from the official nuget.org "python" package plus the
    locked pymobiledevice3, laid out as <Out>\python\python.exe and <Out>\RUNTIME_ID.

.DESCRIPTION
    No venv (a venv records absolute paths and cannot move) and no embeddable zip (no pip, its own sys.path rules):
    the nuget package is a full CPython meant to be unpacked anywhere. Packages go straight into its
    Lib\site-packages with the build interpreter's pip: hashes required, wheels only, nothing resolved beyond the
    lock. Everything is byte-compiled with unchecked-hash .pyc files, which stay valid wherever the folder moves and
    spare the read-only Program Files copy from compiling at every start.

.PARAMETER Python
    A CPython 3.13 x64 on Windows used only to run pip (for example actions/setup-python's).

.PARAMETER Out
    A missing or empty folder to build into.
#>
param(
    [Parameter(Mandatory = $true)] [string]$Python,
    [Parameter(Mandatory = $true)] [string]$Out
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($Exe)) $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

function Get-Info([string]$Exe) {
    "$(& $Exe -I -c "import sys, struct; print('%d.%d %d %s' % (sys.version_info[0], sys.version_info[1], struct.calcsize('P') * 8, sys.platform))")".Trim()
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'build-runtime.ps1 builds the Windows runtime; run it on Windows' }
if ((Get-Info $Python) -ne '3.13 64 win32') { throw "-Python must be CPython 3.13 x64 on Windows; $Python reports '$(Get-Info $Python)'" }

$pin = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime-python.txt')) {
    if ($line -match '^\s*([a-z0-9]+)\s*=\s*(\S+)\s*$') { $pin[$Matches[1]] = $Matches[2] }
}
$version = $pin['version']
$sha = "$($pin['sha256'])".ToLowerInvariant()
if (-not $version -or $sha.Length -ne 64) { throw 'runtime-python.txt needs version=<3.13.x> and sha256=<64 hex digits>' }
$lock = Join-Path $PSScriptRoot 'requirements-win-x64-py313.lock.txt'

if (Test-Path -LiteralPath $Out) {
    if (@(Get-ChildItem -LiteralPath $Out -Force).Count -gt 0) { throw "$Out is not empty" }
} else {
    New-Item -ItemType Directory -Path $Out | Out-Null
}
$Out = (Resolve-Path -LiteralPath $Out).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ('coredevice-runtime-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $nupkg = Join-Path $work "python.$version.zip"
    Invoke-WebRequest -UseBasicParsing -Uri "https://api.nuget.org/v3-flatcontainer/python/$version/python.$version.nupkg" -OutFile $nupkg
    $actual = (Get-FileHash -LiteralPath $nupkg -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha) { throw "python.$version.nupkg has SHA-256 $actual; runtime-python.txt pins $sha" }
    Expand-Archive -LiteralPath $nupkg -DestinationPath (Join-Path $work 'pkg')
    Copy-Item -LiteralPath (Join-Path $work 'pkg\tools') -Destination (Join-Path $Out 'python') -Recurse

    $runtime = Join-Path $Out 'python\python.exe'
    if ((Get-Info $runtime) -ne '3.13 64 win32') { throw "the nuget package is not CPython 3.13 x64: '$(Get-Info $runtime)'" }

    $site = Join-Path $Out 'python\Lib\site-packages'
    Invoke-Native $Python @('-I', '-m', 'pip', 'install', '--disable-pip-version-check', '--no-input', '--no-compile',
        '--require-hashes', '--no-deps', '--only-binary', ':all:', '--target', $site, '-r', $lock)
    Invoke-Native $runtime @('-I', '-m', 'compileall', '-q', '-j', '0', '--invalidation-mode', 'unchecked-hash', (Join-Path $Out 'python'))

    $lockHash = (Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash.ToLowerInvariant()
    $scriptHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
    # changes whenever the interpreter, the lock or the way the folder is built changes
    $id = "py$version-$($sha.Substring(0, 12))-lock-$($lockHash.Substring(0, 12))-build-$($scriptHash.Substring(0, 8))"
    [IO.File]::WriteAllText((Join-Path $Out 'RUNTIME_ID'), "$id`n")
    Write-Host "CoreDevice runtime $id built in $Out"
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

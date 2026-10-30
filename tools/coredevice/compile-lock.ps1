<#
.SYNOPSIS
    Regenerates requirements-win-x64-py313.lock.txt from requirements.in, on Windows, with a pinned pip-tools.

.DESCRIPTION
    Resolves for the interpreter it runs on, so run it with CPython 3.13 x64 on Windows: a lock made on
    another OS would follow that OS's dependency markers. pip-tools is installed at a fixed version into a
    throwaway venv; the result keeps hashes for every file of every pinned release and only considers
    releases with wheels, as setup.ps1 installs with --only-binary.

.PARAMETER Python
    Absolute path to a Python 3.13 x64 interpreter on Windows.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Python
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$PipToolsVersion = '7.6.2'

function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($Exe)) $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'compile-lock.ps1 resolves for Windows; run it on Windows' }
if (-not [IO.Path]::IsPathRooted($Python) -or -not (Test-Path -LiteralPath $Python -PathType Leaf)) {
    throw "-Python must be the absolute path of an existing python.exe (got '$Python')"
}
$info = & $Python -I -c "import sys, struct; print('%d.%d %d' % (sys.version_info[0], sys.version_info[1], struct.calcsize('P') * 8))"
if ("$info".Trim() -ne '3.13 64') { throw "need CPython 3.13 x64; $Python reports '$("$info".Trim())'" }

$tools = Join-Path ([IO.Path]::GetTempPath()) ('coredevice-lock-' + [guid]::NewGuid().ToString('N'))
Push-Location $PSScriptRoot
try {
    Invoke-Native $Python @('-I', '-m', 'venv', $tools)
    $toolsPython = Join-Path $tools 'Scripts\python.exe'
    Invoke-Native $toolsPython @('-I', '-m', 'pip', 'install', '--disable-pip-version-check', '--no-input', "pip-tools==$PipToolsVersion")
    # the header records this script instead of the raw pip-compile command line
    $env:CUSTOM_COMPILE_COMMAND = 'pwsh tools/coredevice/compile-lock.ps1 -Python <python 3.13 x64>'
    Invoke-Native $toolsPython @('-I', '-m', 'piptools', 'compile', '--generate-hashes', '--resolver=backtracking',
        '--allow-unsafe', '--strip-extras', '--no-emit-index-url', '--pip-args', '--only-binary=:all:',
        '--output-file', 'requirements-win-x64-py313.lock.txt', 'requirements.in')
    Write-Host 'requirements-win-x64-py313.lock.txt regenerated; run setup.ps1 and the probe smoke tests before committing it'
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $tools -Recurse -Force -ErrorAction SilentlyContinue
}

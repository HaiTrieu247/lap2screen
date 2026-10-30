<#
.SYNOPSIS
    Checks a built CoreDevice runtime after copying it to a fresh folder whose name has a space, so nothing in it
    may depend on where it was built.

.PARAMETER Runtime
    The folder build-runtime.ps1 produced.

.PARAMETER Probe
    Optional MacExtend.CoreDeviceProbe.exe: when given, the probe smoke tests run on the copied runtime too.
#>
param(
    [Parameter(Mandatory = $true)] [string]$Runtime,
    [string]$Probe
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($Exe)) $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

$copy = Join-Path ([IO.Path]::GetTempPath()) ('MacExtend runtime ' + [guid]::NewGuid().ToString('N').Substring(0, 8))
Copy-Item -LiteralPath (Resolve-Path -LiteralPath $Runtime).Path -Destination $copy -Recurse
try {
    $py = Join-Path $copy 'python\python.exe'
    # the Scheduled Task starts tunneld with pythonw.exe
    if (-not (Test-Path -LiteralPath (Join-Path $copy 'python\pythonw.exe'))) { throw 'python\pythonw.exe is missing' }
    if (-not (Test-Path -LiteralPath (Join-Path $copy 'RUNTIME_ID'))) { throw 'RUNTIME_ID is missing' }
    Invoke-Native $py @('-I', (Join-Path $PSScriptRoot 'check_runtime.py'))

    $env:NO_COLOR = '1'
    $serve = & $py -I -X utf8 -u -m pymobiledevice3 developer core-device display serve-web --help | Out-String
    foreach ($option in @('--bind', '--http-port', '--no-audio', '--userspace', '--tunnel')) {
        if ($serve -notmatch [regex]::Escape($option)) { throw "serve-web --help does not list $option" }
    }
    $tunneld = & $py -I -X utf8 -u -m pymobiledevice3 remote tunneld --help | Out-String
    foreach ($option in @('--no-usb', '--no-usbmux', '--no-mobdev2', '--wifi')) {
        if ($tunneld -notmatch [regex]::Escape($option)) { throw "remote tunneld --help does not list $option" }
    }
    # the RemotePairing TCP tunnel needs TLS-PSK from ssl itself (no sslpsk-pmd3 on 3.13)
    Invoke-Native $py @('-I', '-c', "import ssl; c = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT); c.set_ciphers('PSK'); c.set_psk_client_callback(lambda hint: (None, b'k')); print('psk ok')")

    if ($Probe) {
        & (Join-Path $PSScriptRoot 'test-probe.ps1') -Probe $Probe -Python $py
        if ($LASTEXITCODE -ne 0) { throw "probe smoke tests failed ($LASTEXITCODE)" }
    }
    Write-Host 'runtime ok'
}
finally {
    Remove-Item -LiteralPath $copy -Recurse -Force -ErrorAction SilentlyContinue
}

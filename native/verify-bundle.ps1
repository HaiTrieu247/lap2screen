<#
.SYNOPSIS
  Checks that the uxplay bundle runs on a plain Windows machine: no MSYS2 on PATH,
  every required GStreamer element loads from the bundle itself.
.EXAMPLE
  pwsh native/verify-bundle.ps1 -Bundle dist/uxplay
#>
param(
    [Parameter(Mandatory = $true)][string]$Bundle,
    [string]$Elements = (Join-Path $PSScriptRoot 'gst-elements.txt')
)

$Bundle = (Resolve-Path $Bundle).Path
$bin = Join-Path $Bundle 'bin'
$inspect = Join-Path $bin 'gst-inspect-1.0.exe'

# Same environment MacExtend gives uxplay at runtime (Services/UxPlayHost.cs).
$env:PATH = "$bin;$env:SystemRoot\System32;$env:SystemRoot"
$env:GST_PLUGIN_SYSTEM_PATH_1_0 = Join-Path $Bundle 'lib\gstreamer-1.0'
$env:GST_PLUGIN_SCANNER_1_0 = Join-Path $Bundle 'libexec\gstreamer-1.0\gst-plugin-scanner.exe'
$env:GST_REGISTRY_1_0 = Join-Path ([IO.Path]::GetTempPath()) "macextend-verify-$PID.bin"
Remove-Item Env:GST_PLUGIN_PATH, Env:GST_PLUGIN_PATH_1_0, Env:GST_PLUGIN_SYSTEM_PATH -ErrorAction SilentlyContinue

$failures = 0

$version = (& (Join-Path $bin 'uxplay.exe') -v 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $version -notmatch 'UxPlay version') {
    Write-Host "::error::uxplay.exe -v failed (exit $LASTEXITCODE): $version"
    $failures++
} else {
    Write-Host "ok   uxplay.exe: $version"
}

foreach ($raw in Get-Content $Elements) {
    $line = ($raw -replace '#.*$', '').Trim()
    if (-not $line) { continue }
    $optional = $line.StartsWith('?')
    $line = $line.TrimStart('?')
    $element, $plugin = $line.Split('@', 2)

    & $inspect $element *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "ok   $element"
        continue
    }
    if ($plugin) {
        & $inspect $plugin *> $null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "warn $element not registered here (no GPU?), plugin $plugin loads"
            continue
        }
    }
    if ($optional) {
        Write-Host "warn optional $element missing"
    } else {
        Write-Host "::error::required element $element does not load from the bundle"
        $failures++
    }
}

# Running a pipeline proves the core library, registry and scanner work together, not just load.
& (Join-Path $bin 'gst-launch-1.0.exe') -q fakesrc num-buffers=5 ! queue ! fakesink *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host "::error::gst-launch smoke pipeline failed"
    $failures++
} else {
    Write-Host "ok   gst-launch smoke pipeline"
}

Remove-Item $env:GST_REGISTRY_1_0 -ErrorAction SilentlyContinue
$size = (Get-ChildItem $Bundle -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("bundle size: {0:N1} MB" -f $size)

if ($failures) {
    Write-Host "$failures check(s) failed"
    exit 1
}
Write-Host 'bundle verified'

<#
.SYNOPSIS
    CI (elevated windows-latest runner): the elevated modes of MacExtend.exe with the real runtime zip, and the exact
    command line of the Scheduled Task, run for real.

.DESCRIPTION
    Checks, in this order:
      - bad arguments (exit 2, fast, no window or uxplay), and the modes that need a runtime before there is one;
      - --coredevice-setup in an installed copy under C:\Program Files (a dummy unins000.exe beside MacExtend.exe):
        runtime, exactly 4 firewall rules with their properties, and the task whose Execute, WorkingDirectory and
        Arguments must equal what MacExtend.Core builds (the strings are read from the built MacExtend.Core.dll);
      - --coredevice-rules twice (same 4 rules), a missing zip, a tampered zip (previous runtime kept), "-" (no zip);
      - the task's exact command line (the runtime's pythonw.exe + the bootstrap, python folder as working directory)
        started directly with a hostile parent environment (OPENSSL_CONF naming a broken config, a bogus SystemRoot):
        tunneld must answer GET / with {} and exit after GET /shutdown;
      - the same tunneld started through the real task, then replaced / removed while it runs (needs an interactive
        session for the task user; skipped with a warning when the runner has none);
      - an installed copy outside Program Files and a portable copy: setup with a zip is refused (4), "-" gives the 4
        rules, no task, and removes a task left by another copy;
      - --coredevice-remove twice (task and rules gone, idempotent).
    The helper reports only its exit code, so every case asserts the exact code (read from MacExtend.Core's HelperOutcome).

.PARAMETER App
    Folder of the built app (build/MacExtend); copied (without uxplay) to temporary install folders.

.PARAMETER Zip
    The coredevice runtime zip of the same build (its SHA-256 is embedded in that MacExtend.exe).

.PARAMETER RuntimeId
    The RUNTIME_ID of that zip (steps.coredevice.outputs.id).
#>
param(
    [Parameter(Mandatory = $true)] [string]$App,
    [Parameter(Mandatory = $true)] [string]$Zip,
    [Parameter(Mandatory = $true)] [string]$RuntimeId
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'this test runs on Windows only' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'this test needs an elevated (administrator) session'
}
$user = $identity.Name
$App = (Resolve-Path -LiteralPath $App).Path
$Zip = (Resolve-Path -LiteralPath $Zip).Path

# What the app builds, read from the same MacExtend.Core.dll the app uses (netstandard2.0, loaded from bytes so the
# file stays unlocked): the task's argument line and the helper's exit codes. No second copy of either string here.
$null = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $App 'MacExtend.Core.dll')))
$expectedArguments = [MacExtend.Core.WindowsCommandLine]::Join([string[]][MacExtend.Core.CoreDevice.CoreDeviceArguments]::TunneldBootstrap())
$taskPath = [MacExtend.Core.CoreDevice.CoreDeviceSetupScripts]::TaskPath
$taskName = [MacExtend.Core.CoreDevice.CoreDeviceSetupScripts]::TaskName
$group = [MacExtend.Core.CoreDevice.CoreDeviceSetupScripts]::FirewallGroup
function Code([string]$name) { [int][MacExtend.Core.CoreDevice.HelperOutcome]::$name }

# the bootstrap is one argument after -c, in single quotes only; a '%' would be expanded by Task Scheduler
if ($expectedArguments -notmatch '^-I -X utf8 -c "[^"%]+"$') { throw "unexpected task arguments from Core: $expectedArguments" }

$work = Join-Path ([IO.Path]::GetTempPath()) ('MacExtend setup test ' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
$installed = Join-Path $programFiles "MacExtend CI $suffix"     # installed copy, protected folder: gets the task
$outside = Join-Path $work 'installed outside Program Files'     # installed copy, folder a user can change: no task
$portable = Join-Path $work 'portable'                           # no unins*.exe: no task
$copies = @($installed, $outside, $portable)

function Q([string]$value) { '"' + $value + '"' }

function Step([string]$name, [scriptblock]$body) {
    Write-Host "== $name"
    & $body
}

function New-Copy([string]$folder, [bool]$withUninstaller) {
    New-Item -ItemType Directory -Force $folder | Out-Null
    # without uxplay: the helper modes do not need it and it is by far the biggest part
    Get-ChildItem -LiteralPath $App | Where-Object Name -ne 'uxplay' | Copy-Item -Destination $folder -Recurse
    if ($withUninstaller) { [IO.File]::WriteAllBytes((Join-Path $folder 'unins000.exe'), [byte[]]@()) }
}

function Invoke-Helper([string]$Exe, [string[]]$Arguments = @(), [int]$TimeoutSeconds = 180) {
    $p = Start-Process -FilePath $Exe -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    $null = $p.Handle   # keeps the exit code readable after the process is gone
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
        $p.Kill()
        throw "$(Split-Path $Exe -Leaf) $($Arguments -join ' ') did not finish within $TimeoutSeconds s"
    }
    $p.WaitForExit()
    $p.ExitCode
}

function Expect-Exit([string]$what, [int]$actual, [string]$outcome) {
    if ($actual -ne (Code $outcome)) { throw "$what : exit code $actual, expected $outcome ($(Code $outcome))" }
    Write-Host "   ok: $what -> $outcome"
}

function Get-Task { Get-ScheduledTask -TaskPath $taskPath -TaskName $taskName -ErrorAction SilentlyContinue }
function Get-RuleFacts {
    @(foreach ($r in @(Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue)) {
            $app = $r | Get-NetFirewallApplicationFilter
            $port = $r | Get-NetFirewallPortFilter
            $addr = $r | Get-NetFirewallAddressFilter
            [pscustomobject]@{
                Program = [string]$app.Program; Protocol = [string]$port.Protocol; LocalPort = (@($port.LocalPort) -join ',')
                Remote = (@($addr.RemoteAddress) -join ','); Profile = [string]$r.Profile; Direction = [string]$r.Direction
                Action = [string]$r.Action; Enabled = [string]$r.Enabled
            }
        })
}

function Assert-NoLeftovers([string]$folder) {
    foreach ($leftover in @('coredevice.new', 'coredevice.old')) {
        if (Test-Path -LiteralPath (Join-Path $folder $leftover)) { throw "$leftover left in $folder" }
    }
}

# exactly 4 rules, 2 per program (python.exe serves video, pythonw.exe runs tunneld): tunnel video from fc00::/7 on
# any profile, Wi-Fi discovery on UDP 5353 on the Private profile only
function Assert-Rules([string]$pythonDir) {
    $facts = @(Get-RuleFacts)
    if ($facts.Count -ne 4) { throw "expected 4 firewall rules in '$group', found $($facts.Count)" }
    foreach ($exe in @('python.exe', 'pythonw.exe')) {
        $program = Join-Path $pythonDir $exe
        $mine = @($facts | Where-Object { $_.Program -ieq $program })
        if ($mine.Count -ne 2) { throw "expected 2 rules for $program, found $($mine.Count)" }
        foreach ($f in $mine) {
            if ($f.Direction -ne 'Inbound' -or $f.Action -ne 'Allow' -or $f.Enabled -ne 'True' -or $f.Protocol -ne 'UDP') {
                throw "rule for $program has $($f.Direction)/$($f.Action)/$($f.Enabled)/$($f.Protocol)"
            }
        }
        $video = @($mine | Where-Object { $_.Remote -like 'fc00::/7*' -and $_.LocalPort -eq 'Any' -and $_.Profile -eq 'Any' })
        $discovery = @($mine | Where-Object { $_.LocalPort -eq '5353' -and $_.Profile -eq 'Private' })
        if ($video.Count -ne 1 -or $discovery.Count -ne 1) {
            throw "rules for $program are not one video rule (fc00::/7, Any) and one discovery rule (5353, Private): $($mine | Out-String)"
        }
    }
}

function Assert-Task([string]$pythonDir) {
    $task = Get-Task
    if (-not $task) { throw 'task missing' }
    if (($task.Principal.UserId -split '\\')[-1] -ine ($user -split '\\')[-1]) { throw "task user '$($task.Principal.UserId)', expected '$user'" }
    if ([string]$task.Principal.RunLevel -ne 'Highest') { throw "task run level $($task.Principal.RunLevel)" }
    if ([string]$task.Principal.LogonType -ne 'Interactive') { throw "task logon type $($task.Principal.LogonType)" }
    if (($task.Triggers | Where-Object { $_ } | Measure-Object).Count -ne 0) { throw 'the task must have no trigger (it starts on demand)' }
    if (@($task.Actions).Count -ne 1) { throw "task has $(@($task.Actions).Count) actions" }
    $action = $task.Actions[0]
    $pythonw = Join-Path $pythonDir 'pythonw.exe'
    # Task Scheduler joins Execute and Arguments without adding quotes, so the quotes belong to Execute
    if ($action.Execute -cne (Q $pythonw)) { throw "task Execute '$($action.Execute)', expected '$(Q $pythonw)'" }
    if ($action.WorkingDirectory -ine $pythonDir) { throw "task WorkingDirectory '$($action.WorkingDirectory)', expected '$pythonDir'" }
    if ($action.Arguments -cne $expectedArguments) { throw "task Arguments differ from WindowsCommandLine.Join(TunneldBootstrap()):`n got:      $($action.Arguments)`n expected: $expectedArguments" }
}

function Assert-State([string]$folder, [bool]$withTask) {
    $pythonDir = Join-Path $folder 'coredevice\python'
    Assert-Rules $pythonDir
    if ($withTask) { Assert-Task $pythonDir } elseif (Get-Task) { throw 'a task exists, none expected' }
}

function Assert-Runtime([string]$folder) {
    foreach ($exe in @('python.exe', 'pythonw.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $folder "coredevice\python\$exe"))) { throw "$exe not installed in $folder" }
    }
    $id = (Get-Content -LiteralPath (Join-Path $folder 'coredevice\RUNTIME_ID') -Raw).Trim()
    if ($id -ne $RuntimeId) { throw "RUNTIME_ID '$id', expected '$RuntimeId'" }
    Assert-NoLeftovers $folder
}

function Assert-NoHelperProcesses {
    $left = @(Get-Process -Name 'MacExtend', 'uxplay' -ErrorAction SilentlyContinue)
    if ($left.Count -ne 0) { throw "a MacExtend or uxplay process is running after the helper: $($left.Id -join ',')" }
}

# --- tunneld on 127.0.0.1:49151 -------------------------------------------------------------------------------------

function Test-TunneldPort {
    $c = [Net.Sockets.TcpClient]::new()
    try { $c.ConnectAsync('127.0.0.1', 49151).Wait(1000) -and $c.Connected } catch { $false } finally { $c.Dispose() }
}

function Get-TunneldRoot {
    try { ([string](Invoke-WebRequest -Uri 'http://127.0.0.1:49151/' -UseBasicParsing -TimeoutSec 3).Content) -replace '\s', '' } catch { $null }
}

function Wait-TunneldAnswer([int]$Seconds, [Diagnostics.Process]$Process) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ((Get-TunneldRoot) -eq '{}') { return $true }
        if ($Process -and $Process.HasExited) { throw "tunneld exited early with code $($Process.ExitCode)" }
        Start-Sleep -Seconds 1
    }
    $false
}

function Wait-PortClosed([int]$Seconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not (Test-TunneldPort)) { return $true }
        Start-Sleep -Milliseconds 500
    }
    $false
}

function Stop-Tunneld { try { $null = Invoke-WebRequest -Uri 'http://127.0.0.1:49151/shutdown' -UseBasicParsing -TimeoutSec 5 } catch { } }

# Runs $Body with a hostile parent environment (OPENSSL_CONF naming a config that breaks TLS, a bogus SystemRoot and
# windir) and puts the real environment back afterwards; returns what $Body returns.
function Invoke-HostileEnvironment([scriptblock]$Body) {
    $brokenConfig = Join-Path $work 'broken-openssl.cnf'
    Set-Content -LiteralPath $brokenConfig -Encoding ascii -Value @(
        'openssl_conf = ci_init', '', '[ci_init]', 'ssl_conf = ci_ssl', '', '[ci_ssl]', 'system_default = ci_system', '',
        '[ci_system]', 'CipherString = NO-SUCH-CIPHER-MACEXTEND-CI', 'MinProtocol = NoSuchProtocol')
    $bogusRoot = Join-Path $work 'no such windows'
    $names = @('OPENSSL_CONF', 'SystemRoot', 'windir')
    $saved = @{}
    foreach ($n in $names) { $saved[$n] = [Environment]::GetEnvironmentVariable($n, 'Process') }
    try {
        [Environment]::SetEnvironmentVariable('OPENSSL_CONF', $brokenConfig, 'Process')
        [Environment]::SetEnvironmentVariable('SystemRoot', $bogusRoot, 'Process')
        [Environment]::SetEnvironmentVariable('windir', $bogusRoot, 'Process')
        & $Body
    }
    finally {
        foreach ($n in $names) { [Environment]::SetEnvironmentVariable($n, $saved[$n], 'Process') }
    }
}

# The environment reset of the bootstrap, checked directly: the bootstrap's own code up to and including the statement
# that sets SystemRoot, windir, PATH and OPENSSL_CONF (cut from Core's string at "import runpy", no second copy), then a
# print of the result. Under the hostile parent environment the child must see the rebuilt values.
function Assert-EnvironmentReset([string]$pythonDir) {
    $py = Join-Path $pythonDir 'python.exe'
    $code = [MacExtend.Core.CoreDevice.CoreDeviceArguments]::TunneldBootstrap()[4]
    $cut = $code.IndexOf('import runpy')
    if ($cut -lt 0) { throw "the bootstrap has no 'import runpy' statement to cut at" }
    $prefix = $code.Substring(0, $cut)
    if ($prefix -notmatch 'OPENSSL_CONF=os\.devnull\); $') { throw "the bootstrap's reset prefix does not end with the OPENSSL_CONF statement: ...$($prefix.Substring([Math]::Max(0, $prefix.Length - 60)))" }
    $print = "print(os.environ.get('OPENSSL_CONF'), os.environ.get('SystemRoot'), os.environ.get('windir'), os.environ.get('PATH'), sep='|')"
    $win = [IO.Path]::GetDirectoryName([Environment]::SystemDirectory)
    $lines = @(Invoke-HostileEnvironment { & $py -I -X utf8 -c ($prefix + $print) })
    if ($LASTEXITCODE -ne 0) { throw "the reset prefix exited $LASTEXITCODE under the hostile environment: $($lines -join ' / ')" }
    $parts = ([string]$lines[-1]).Split('|')
    if ($parts.Count -ne 4) { throw "unexpected output of the reset check: $($lines -join ' / ')" }
    if ($parts[0] -ine 'nul') { throw "OPENSSL_CONF is '$($parts[0])' after the reset, expected nul (os.devnull)" }
    if ($parts[1] -ine $win -or $parts[2] -ine $win) { throw "SystemRoot/windir are '$($parts[1])' / '$($parts[2])' after the reset, expected '$win'" }
    $expectedPath = (Join-Path $win 'System32') + ';' + $win
    if ($parts[3] -ine $expectedPath) { throw "PATH is '$($parts[3])' after the reset, expected '$expectedPath'" }
    Write-Host '   ok: the bootstrap rebuilt OPENSSL_CONF, SystemRoot, windir and PATH from a hostile parent environment'
}

# Negative control for the OPENSSL_CONF attack: without the reset, the broken config should make TLS fail.
function Test-OpenSslAttackReproduces([string]$pythonDir) {
    $py = Join-Path $pythonDir 'python.exe'
    $null = Invoke-HostileEnvironment { & $py -I -c 'import ssl; ssl.create_default_context()' 2>&1 | Out-Null }
    $failed = $LASTEXITCODE -ne 0
    # the expected failure must not become the step's result: the runner ends a pwsh step with exit $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if ($failed) {
        Write-Host '   ok: without the reset the broken OPENSSL_CONF makes ssl fail, so the reset is what protects tunneld'
    }
    else {
        Write-Host '::warning title=CoreDevice OPENSSL_CONF::the OPENSSL_CONF attack was not reproduced on this runner (ssl worked with the broken config); the protection rests on the reset check alone'
    }
}

# The task's exact command line: the runtime's pythonw.exe, the joined bootstrap arguments, the python folder as the
# working directory, started under the hostile parent environment.
function Invoke-TunneldSmoke([string]$pythonDir) {
    if (Test-TunneldPort) { throw 'something already listens on 127.0.0.1:49151' }
    Assert-EnvironmentReset $pythonDir
    Test-OpenSslAttackReproduces $pythonDir
    $p = Invoke-HostileEnvironment {
        Start-Process -FilePath (Join-Path $pythonDir 'pythonw.exe') -ArgumentList $expectedArguments -WorkingDirectory $pythonDir -PassThru
    }
    $null = $p.Handle
    try {
        if (-not (Wait-TunneldAnswer 60 $p)) { throw 'tunneld did not answer GET / with {} within 60 s' }
        Write-Host '   ok: GET / returned {} with a broken OPENSSL_CONF and a bogus SystemRoot in the parent environment'
        Stop-Tunneld
        if (-not $p.WaitForExit(30000)) { throw 'tunneld did not exit within 30 s of GET /shutdown' }
        Write-Host "   ok: tunneld exited after GET /shutdown (code $($p.ExitCode))"
        if (-not (Wait-PortClosed 10)) { throw 'port 49151 still listens after tunneld exited' }
    }
    finally {
        if (-not $p.HasExited) { $p.Kill() }
    }
}

# Starts tunneld through the registered task; false when this session cannot (an Interactive task needs its user logged on).
function Start-TaskTunneld {
    $null = Start-ScheduledTask -TaskPath $taskPath -TaskName $taskName
    Wait-TunneldAnswer 30 $null
}

# --------------------------------------------------------------------------------------------------------------------


$exe = Join-Path $installed 'MacExtend.exe'
$pythonDir = Join-Path $installed 'coredevice\python'
$userArg = Q $user
$zipArg = Q $Zip
$taskRuns = $false

try {
    New-Copy $installed $true
    New-Copy $outside $true
    New-Copy $portable $false

    Step 'start from a clean machine' {
        Expect-Exit 'remove with nothing to remove' (Invoke-Helper $exe @('--coredevice-remove')) 'Ok'
        if (@(Get-RuleFacts).Count -ne 0 -or (Get-Task)) { throw 'rules or task exist after remove' }
    }

    Step 'bad arguments: exit 2 within seconds, no window, no uxplay' {
        $cases = @(
            @('--coredevice-setup'),
            @('--coredevice-setup', '-', $userArg),
            @('--coredevice-setup', 'relative\runtime.zip', $userArg, '1'),
            @('--coredevice-setup', '\\server\share\runtime.zip', $userArg, '1'),
            @('--coredevice-setup', '-', $userArg, '2'),
            @('--coredevice-rules', 'extra'),
            @('--coredevice-remove', 'extra'))
        foreach ($c in $cases) {
            $watch = [Diagnostics.Stopwatch]::StartNew()
            Expect-Exit ($c -join ' ') (Invoke-Helper $exe $c 30) 'BadArguments'
            if ($watch.Elapsed.TotalSeconds -gt 20) { throw "bad arguments took $([int]$watch.Elapsed.TotalSeconds) s" }
        }
        # CoreDeviceProcessHost.HelperBadArguments: the signal-ctrl-c helper path runs before any UI
        $code = Invoke-Helper $exe @('signal-ctrl-c', '0') 30
        if ($code -ne 12) { throw "signal-ctrl-c 0 exited $code, expected 12" }
        Assert-NoHelperProcesses
    }

    Step 'no runtime yet: rules and "-" report RuntimeMissing and change nothing' {
        Expect-Exit '--coredevice-rules' (Invoke-Helper $exe @('--coredevice-rules')) 'RuntimeMissing'
        Expect-Exit '--coredevice-setup - ... 1' (Invoke-Helper $exe @('--coredevice-setup', '-', $userArg, '1')) 'RuntimeMissing'
        if (@(Get-RuleFacts).Count -ne 0 -or (Get-Task)) { throw 'rules or task were made without a runtime' }
    }

    Step 'a zip that does not exist' {
        Expect-Exit 'setup with a missing zip' (Invoke-Helper $exe @('--coredevice-setup', (Q (Join-Path $work 'no such folder\runtime.zip')), $userArg, '1')) 'ZipNotFound'
        if (Test-Path -LiteralPath (Join-Path $installed 'coredevice')) { throw 'a runtime folder appeared' }
    }

    Step 'first setup in the installed copy under Program Files' {
        Expect-Exit 'setup with the real zip' (Invoke-Helper $exe @('--coredevice-setup', $zipArg, $userArg, '1')) 'Ok'
        Assert-Runtime $installed
        Assert-State $installed $true
        Write-Host '   ok: runtime, 4 firewall rules and the task (Execute, WorkingDirectory, Arguments) are as built by MacExtend.Core'
    }

    Step 'rules only, twice, and after the rules were deleted' {
        Expect-Exit '--coredevice-rules (1)' (Invoke-Helper $exe @('--coredevice-rules')) 'Ok'
        Assert-State $installed $true
        Expect-Exit '--coredevice-rules (2)' (Invoke-Helper $exe @('--coredevice-rules')) 'Ok'
        Assert-State $installed $true
        Get-NetFirewallRule -Group $group | Remove-NetFirewallRule
        Expect-Exit '--coredevice-rules after the rules were deleted' (Invoke-Helper $exe @('--coredevice-rules')) 'Ok'
        Assert-State $installed $true
    }

    Step 'tunneld: the task command line, hostile parent environment' {
        Invoke-TunneldSmoke $pythonDir
    }

    Step 'setup without a zip ("-") keeps the runtime' {
        Expect-Exit 'setup - ... 1' (Invoke-Helper $exe @('--coredevice-setup', '-', $userArg, '1')) 'Ok'
        Assert-Runtime $installed
        Assert-State $installed $true
    }

    Step 'tampered zip: refused, previous runtime intact' {
        $bad = Join-Path $work 'tampered.zip'
        Copy-Item -LiteralPath $Zip -Destination $bad
        $stream = [IO.File]::Open($bad, [IO.FileMode]::Append)
        try { $stream.WriteByte(0) } finally { $stream.Dispose() }
        Expect-Exit 'setup with a zip plus one byte' (Invoke-Helper $exe @('--coredevice-setup', (Q $bad), $userArg, '1')) 'HashMismatch'
        Assert-Runtime $installed
        Assert-State $installed $true
    }

    Step 'replace the runtime while the task runs tunneld' {
        $started = $false
        $startError = $null
        try { $started = Start-TaskTunneld } catch { $startError = $_.Exception.Message }
        $script:taskRuns = $started
        if ($started) {
            Write-Host '   ok: the registered task started tunneld (GET / returned {})'
            Expect-Exit 'setup while tunneld runs' (Invoke-Helper $exe @('--coredevice-setup', $zipArg, $userArg, '1')) 'Ok'
            if (-not (Wait-PortClosed 10)) { throw 'tunneld still listens after the setup replaced the runtime' }
            Assert-Runtime $installed
            Assert-State $installed $true
            if (-not (Start-TaskTunneld)) { throw 'the task does not start tunneld from the replaced runtime' }
            Stop-Tunneld
            if (-not (Wait-PortClosed 30)) { throw 'tunneld did not stop after GET /shutdown' }
            Write-Host '   ok: runtime replaced while running, and the new runtime starts through the task'
        }
        else {
            if ($startError) { Write-Host "   Start-ScheduledTask failed: $startError" }
            $info = Get-ScheduledTaskInfo -TaskPath $taskPath -TaskName $taskName -ErrorAction SilentlyContinue
            if ($info) { Write-Host "   task LastTaskResult=$($info.LastTaskResult) LastRunTime=$($info.LastRunTime)" }
            # the only real start of the registered task in CI: skipping it must be a deliberate, visible choice
            if ($env:MACEXTEND_CI_ALLOW_TASK_SKIP -ne '1') {
                throw 'the registered Interactive task did not start tunneld on this runner (set MACEXTEND_CI_ALLOW_TASK_SKIP=1 to skip knowingly)'
            }
            Write-Host '::warning title=CoreDevice task::MACEXTEND_CI_ALLOW_TASK_SKIP=1: the registered task was not started; replace-while-running and remove-while-running were not exercised'
        }
    }

    # the task made above belongs to the installed copy; a copy that may not have a task must remove it
    foreach ($copy in @(@($outside, 'an installed copy outside Program Files'), @($portable, 'a portable copy'))) {
        $folder = $copy[0]
        Step "$($copy[1]): no task, refuses to stage a runtime, rules only" {
            Expect-Exit 'back to the protected copy: setup -' (Invoke-Helper $exe @('--coredevice-setup', '-', $userArg, '1')) 'Ok'
            Assert-State $installed $true
            Copy-Item -LiteralPath (Join-Path $installed 'coredevice') -Destination (Join-Path $folder 'coredevice') -Recurse
            $other = Join-Path $folder 'MacExtend.exe'
            Expect-Exit 'setup with a zip' (Invoke-Helper $other @('--coredevice-setup', $zipArg, $userArg, '1')) 'NotProtectedLocation'
            Assert-NoLeftovers $folder
            Expect-Exit 'setup - ... 1 (TASK=1 is only the caller asking)' (Invoke-Helper $other @('--coredevice-setup', '-', $userArg, '1')) 'Ok'
            Assert-State $folder $false
            Expect-Exit '--coredevice-rules' (Invoke-Helper $other @('--coredevice-rules')) 'Ok'
            Assert-State $folder $false
        }
    }

    Step 'remove: task and rules go, a second remove is fine' {
        Expect-Exit 'setup - in the protected copy' (Invoke-Helper $exe @('--coredevice-setup', '-', $userArg, '1')) 'Ok'
        Assert-State $installed $true
        $running = $taskRuns -and (Start-TaskTunneld)
        Expect-Exit '--coredevice-remove' (Invoke-Helper $exe @('--coredevice-remove')) 'Ok'
        if (@(Get-RuleFacts).Count -ne 0) { throw 'firewall rules left behind' }
        if (Get-Task) { throw 'task left behind' }
        if ($running -and -not (Wait-PortClosed 10)) { throw 'remove left the task running tunneld' }
        Expect-Exit '--coredevice-remove again' (Invoke-Helper $exe @('--coredevice-remove')) 'Ok'
        if (-not (Test-Path -LiteralPath (Join-Path $installed 'coredevice\python\python.exe'))) { throw 'remove must leave the runtime to the uninstaller' }
    }

    Write-Host 'coredevice setup ok'
}
finally {
    Stop-Tunneld
    try { $null = Invoke-Helper (Join-Path $installed 'MacExtend.exe') @('--coredevice-remove') 120 } catch { Write-Host "cleanup: remove failed: $($_.Exception.Message)" }
    foreach ($copy in $copies) {
        Get-Process -Name 'python', 'pythonw' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($copy, [StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $copy -Recurse -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

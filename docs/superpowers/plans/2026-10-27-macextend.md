# MacExtend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows app that turns a Windows laptop into an AirPlay "Use as Separate Display" target for a Mac.

**Architecture:** UxPlay (C, GPLv3, pinned commit) is built in MSYS2 UCRT64 on GitHub Actions and bundled with only the GStreamer plugins it needs. A WPF tray app (.NET Framework 4.8) supervises `uxplay.exe` as a hidden child process, computes its arguments from settings, tracks connection state, and handles firewall, network profile, sleep/wake and restarts. All platform-independent logic lives in `MacExtend.Core` (netstandard2.0) and is unit tested on macOS and CI.

**Tech Stack:** C# (LangVersion latest), WPF + WinForms NotifyIcon on net48, netstandard2.0 core, xUnit on net10.0, MSYS2 UCRT64 (gcc, cmake, ninja, GStreamer 1.2x), Inno Setup 6, GitHub Actions.

**Spec:** No written spec (user waived it). The design agreed in chat is restated in Global Constraints below.

## Global Constraints

- Target machine: Windows 10/11 x64 laptop, 1920x1080 @ up to 165 Hz. ARM64 out of scope.
- Upstream: `https://github.com/FDH2/UxPlay` commit `28ed70a9876fe3f5864479f4e0d07abe4678383a` (UxPlay 1.74, internal mDNS, no Bonjour).
- UxPlay must be built with `-DNO_MARCH_NATIVE=ON` (CI CPU differs from user CPU).
- Default uxplay args: `-n <name> -nh -s <W>x<H>@<Hz> -fps <fps> -vsync no -vs d3d11videosink -fs -nohold -key <keyfile>`; plus `-pin <nnnn> -reg <regfile>` when PIN on; `-as 0` when audio off; `-h265` only in custom mode when enabled.
- Auto resolution: native size fitted inside 1920x1080 keeping aspect, even numbers, refresh = min(native, 60).
- Refresh reported to the Mac is capped at 60 in Auto mode; custom allows 24..240.
- Device name sent to uxplay is ASCII only (Windows `main(argc, argv)` uses the ANSI code page). Vietnamese diacritics are folded (`Máy của Thịnh` -> `May cua Thinh`).
- File paths passed to uxplay containing non-ASCII are converted to 8.3 short paths.
- UI text is Vietnamese. No emoji anywhere (UI, tray, logs, docs). Icons come from the Segoe Fluent Icons / Segoe MDL2 Assets font.
- Theme follows Windows light/dark setting, switching live.
- Product name `MacExtend`; never use "AirPlay" in the product name (wording like "nhận AirPlay" in descriptions is fine).
- License GPL-3.0-or-later for the whole repo.
- User data: `%APPDATA%\MacExtend\` (settings.ini, uxplay.pem, uxplay.register). Logs and GStreamer registry: `%LOCALAPPDATA%\MacExtend\`.
- Bundle layout: `<app>\MacExtend.exe`, `<app>\uxplay\bin\uxplay.exe` + DLLs, `<app>\uxplay\lib\gstreamer-1.0\*.dll`, `<app>\uxplay\libexec\gstreamer-1.0\gst-plugin-scanner.exe`.
- The user pushes to GitHub themselves; we only create local commits.

## Review Focus

1. Vietnamese device name or a Windows user name with diacritics -> uxplay receives mangled argv or cannot open its key file. Expect: name folded to ASCII, paths short-pathed. Tests: `DeviceNameTests`, `UxPlayArgumentsTests.NonAsciiPathUsesResolver`.
2. Windows auto-created a *block* firewall rule (user clicked Cancel on the first prompt) or Wi-Fi is on the Public profile -> Mac never sees the device. Expect: status row says so and one click fixes it (removes block rules, adds allow rule, or switches profile). Test: `FirewallStatusParserTests`, `NetworkProfileParserTests`.
3. Quit, Windows shutdown or log-off while the Mac is connected -> uxplay must get Ctrl+C so it sends the mDNS goodbye, and never stays orphaned. Expect: graceful stop within 3 s, then kill. Covered by `UxPlayHost` design plus manual test T9 in `docs/TESTING.md`.
4. Sleep/wake or Wi-Fi flapping -> restart storm. Expect: network events debounced 5 s, crash restarts back off 1/2/5/10/30 s and give up after 5 failures within 2 minutes. Test: `RestartPolicyTests`, `DebouncerTests`.
5. Corrupt or hand-edited settings.ini (garbage, empty, out-of-range resolution) -> app must start with defaults/clamped values, never crash. Test: `SettingsStoreTests.GarbageFallsBackToDefaults`, `ClampsOutOfRange`.

---

## File Structure

```
MacExtend/
  .github/workflows/build.yml          CI: uxplay runtime job, app job, release on tag
  .gitattributes / .gitignore / LICENSE / README.md
  MacExtend.sln
  native/
    uxplay.ref                         upstream repo URL + pinned commit
    gst-elements.txt                   GStreamer elements the bundle must provide
    build-uxplay.sh                    MSYS2: clone, build uxplay.exe
    bundle-runtime.sh                  MSYS2: copy exe, plugins, DLL closure into dist/uxplay
    verify-bundle.ps1                  plain Windows: run bundle with clean PATH, gst-inspect each element
  src/MacExtend.Core/                  netstandard2.0, no Windows APIs
    AppSettings.cs                     settings model + defaults + Clamp()
    SettingsStore.cs                   key=value load/save
    DeviceName.cs                      ASCII folding/sanitizing
    Pin.cs                             PIN generate/validate
    DisplayMode.cs                     W/H/Hz value type
    ResolutionPolicy.cs                settings + native mode -> requested mode, h265 flag
    WindowsCommandLine.cs              argv quoting (net48 has no ArgumentList)
    UxPlayArguments.cs                 builds argv list
    UxPlayLogParser.cs                 log line -> event
    ConnectionTracker.cs               log events + TCP samples -> state
    RestartPolicy.cs                   crash backoff
    Debouncer.cs                       time-based coalescing (clock injected)
    RotatingLog.cs                     size-capped log file
    FirewallStatusParser.cs            parse PowerShell probe output
    NetworkProfileParser.cs            parse PowerShell probe output
  tests/MacExtend.Core.Tests/          xUnit, net10.0
  src/MacExtend.App/                   net48 WPF (+WinForms for NotifyIcon)
    App.xaml(.cs)                      startup, single instance, theme
    AppPaths.cs                        install/data/log locations
    Themes/Colors.Light.xaml, Colors.Dark.xaml, Controls.xaml, ThemeManager.cs
    Native/Win32.cs                    P/Invoke declarations
    Services/DisplayInfo.cs            EnumDisplaySettings
    Services/ShortPath.cs              GetShortPathNameW
    Services/UxPlayHost.cs             process start, env, output, graceful stop
    Services/TcpConnections.cs         GetExtendedTcpTable per PID
    Services/KeepAwake.cs              SetThreadExecutionState
    Services/AutoStart.cs              HKCU Run
    Services/PowerShellRunner.cs       run script, optionally elevated
    Services/Firewall.cs, NetworkProfile.cs
    Services/ReceiverController.cs     orchestration and state
    ViewModels/MainViewModel.cs, RelayCommand.cs, ObservableObject.cs
    Views/MainWindow.xaml(.cs), Views/DeviceIllustration.xaml(.cs)
    Tray/TrayIcon.cs, Tray/ModernMenuRenderer.cs
    Assets/*.ico                       generated by tools/make_icons.py
  tools/make_icons.py                  pure-Python ICO generator (no Pillow)
  installer/MacExtend.iss              Inno Setup script
  docs/TESTING.md                      manual acceptance checklist
```

---

### Task 1: Repo scaffold, Core settings model and store

**Files:**
- Create: `.gitignore`, `.gitattributes`, `MacExtend.sln`, `src/MacExtend.Core/MacExtend.Core.csproj`, `src/MacExtend.Core/AppSettings.cs`, `src/MacExtend.Core/SettingsStore.cs`
- Test: `tests/MacExtend.Core.Tests/MacExtend.Core.Tests.csproj`, `tests/MacExtend.Core.Tests/SettingsStoreTests.cs`

**Interfaces (produces):**
```csharp
public enum ResolutionMode { Auto, Custom }
public sealed class AppSettings {
    public string DeviceName { get; set; } = "";       // empty => default from machine name
    public ResolutionMode ResolutionMode { get; set; } = ResolutionMode.Auto;
    public int CustomWidth { get; set; } = 1920;       // clamp 640..4096
    public int CustomHeight { get; set; } = 1080;      // clamp 480..2304
    public int CustomRefresh { get; set; } = 60;       // clamp 24..240
    public bool CustomH265 { get; set; }
    public int Fps { get; set; } = 60;                 // 30 or 60
    public bool AudioEnabled { get; set; } = true;
    public bool PinEnabled { get; set; } = true;
    public string Pin { get; set; } = "";              // empty => generate
    public bool StartWithWindows { get; set; }
    public AppSettings Clone();
    public void Clamp();
}
public static class SettingsStore {
    public static AppSettings Parse(string text);      // never throws
    public static string Serialize(AppSettings s);
    public static AppSettings Load(string path);       // missing/unreadable => defaults
    public static void Save(string path, AppSettings s); // atomic: write tmp then replace
}
```

- [ ] Step 1: tests: round-trip all fields; `Parse("")` equals defaults; garbage lines (`"%%%"`, `"Fps=abc"`, `"=x"`) ignored; `CustomWidth=99999` clamps to 4096; `Fps=45` becomes 60; unknown keys ignored; `Load` of missing file returns defaults; `Save` then `Load` round-trips; values with `=` and spaces in DeviceName survive.
- [ ] Step 2: run `dotnet test` -> FAIL (types missing).
- [ ] Step 3: implement. Format: one `Key=Value` per line, `#` comments, UTF-8 without BOM, booleans `true/false`, enums by name (case-insensitive).
- [ ] Step 4: `dotnet test` -> PASS.
- [ ] Step 5: commit `feat(core): settings model and store`.

### Task 2: Device name, PIN, display mode, resolution policy

**Files:** `src/MacExtend.Core/DeviceName.cs`, `Pin.cs`, `DisplayMode.cs`, `ResolutionPolicy.cs`; tests `DeviceNameTests.cs`, `PinTests.cs`, `ResolutionPolicyTests.cs`

**Interfaces:**
```csharp
public static class DeviceName {
    public const int MaxLength = 40;
    public static string Sanitize(string input, string fallback); // ASCII fold, allowed [A-Za-z0-9 ._'-()], collapse spaces, trim, cut to MaxLength; empty => Sanitize(fallback) or "MacExtend"
    public static string DefaultFor(string machineName);          // "MacExtend-" + Sanitize(machineName)
}
public static class Pin {
    public static string Generate(Random rng);  // "1000".."9999"
    public static bool IsValid(string pin);     // exactly 4 ASCII digits, not "0000"
}
public readonly struct DisplayMode : IEquatable<DisplayMode> {
    public DisplayMode(int width, int height, int refreshHz);
    public int Width { get; } public int Height { get; } public int RefreshHz { get; }
    public override string ToString();          // "1920x1080@60"
}
public sealed class RequestedVideo { public DisplayMode Mode; public bool H265; }
public static class ResolutionPolicy {
    public static RequestedVideo Resolve(AppSettings s, DisplayMode native);
}
```
- [ ] Step 1: tests:
  - `Sanitize("Máy của Thịnh", "x") == "May cua Thinh"`; `"Đường"` -> `"Duong"`; `"a\"b;c"` -> `"abc"`; 60 chars -> 40; `"   "` -> fallback; non-Latin only (`"日本"`) -> fallback.
  - `DefaultFor("DESKTOP-ÂB12") == "MacExtend-DESKTOP-AB12"`.
  - `Pin.Generate` 1000 iterations all valid and 4 digits; `IsValid("12a4") == false`, `IsValid("0000") == false`.
  - Auto: native 1920x1080@165 -> 1920x1080@60, H265 false; 2560x1600@60 -> 1728x1080@60; 1366x768@60 -> 1366x768@60; 1280x1024@75 -> 1280x1024@60; 3840x2160@60 -> 1920x1080@60; refresh 0 (unknown) -> 60; odd result widths rounded down to even.
  - Custom: 2560x1440@120 + H265 -> exactly that, H265 true; custom H265 false keeps false.
- [ ] Step 2: run -> FAIL. Step 3: implement (fold with `NormalizationForm.FormD` + drop `UnicodeCategory.NonSpacingMark`, map `đ/Đ`). Step 4: PASS. Step 5: commit `feat(core): device name, pin, resolution policy`.

### Task 3: Command-line quoting and uxplay argument builder

**Files:** `src/MacExtend.Core/WindowsCommandLine.cs`, `UxPlayArguments.cs`; tests `WindowsCommandLineTests.cs`, `UxPlayArgumentsTests.cs`

**Interfaces:**
```csharp
public static class WindowsCommandLine {
    public static string Quote(string arg);                       // CommandLineToArgvW-compatible
    public static string Join(IEnumerable<string> args);
}
public sealed class UxPlayPaths { public string KeyFile; public string RegisterFile; }
public static class UxPlayArguments {
    public static IReadOnlyList<string> Build(AppSettings s, string deviceName, RequestedVideo video,
                                              UxPlayPaths paths, Func<string, string> pathResolver);
}
```
- [ ] Step 1: tests:
  - Quote: `abc` -> `abc`; `a b` -> `"a b"`; empty -> `""`; `a"b` -> `"a\"b"`; `C:\x y\` -> `"C:\x y\\"`; `a\\b` unchanged.
  - Build defaults (PIN on, audio on, auto 1920x1080@60, fps 60) equals exactly `-n, MacExtend-PC, -nh, -s, 1920x1080@60, -fps, 60, -vsync, no, -vs, d3d11videosink, -fs, -nohold, -key, K, -pin, 1234, -reg, R`.
  - PIN off -> no `-pin`/`-reg`; audio off -> contains `-as`, `0`; H265 -> contains `-h265`; fps 30 -> `-fps 30`.
  - `NonAsciiPathUsesResolver`: KeyFile `C:\Users\Thịnh\k.pem` passes through `pathResolver` and the resolved value appears.
- [ ] Step 2: FAIL. Step 3: implement. Step 4: PASS. Step 5: commit `feat(core): uxplay argument builder`.

### Task 4: Log parser, connection tracker, restart policy, debouncer, rotating log, probe parsers

**Files:** `UxPlayLogParser.cs`, `ConnectionTracker.cs`, `RestartPolicy.cs`, `Debouncer.cs`, `RotatingLog.cs`, `FirewallStatusParser.cs`, `NetworkProfileParser.cs` + matching tests.

**Interfaces:**
```csharp
public enum LogEventKind { None, ClientConnecting, ConnectionLost, Error }
public sealed class LogEvent { public LogEventKind Kind; public string ClientName; public string ClientModel; public string Text; }
public static class UxPlayLogParser { public static LogEvent Parse(string line); }

public enum ReceiverPhase { Stopped, Starting, Ready, Connected, Failed }
public sealed class ConnectionTracker {
    public string ClientName { get; }
    public bool IsConnected { get; }
    public bool OnLog(LogEvent e);          // returns true if IsConnected/ClientName changed
    public bool OnTcpSample(int established); // 2 consecutive zero samples while connected => disconnected
    public void Reset();
}
public sealed class RestartPolicy {
    public RestartPolicy(Func<DateTime> clock);
    public TimeSpan? NextDelay();            // records a failure; null => give up
    public void ReportHealthy();             // process ran > 30 s: reset counters
}
public sealed class Debouncer {
    public Debouncer(TimeSpan quiet, Func<DateTime> clock);
    public void Signal();
    public bool ShouldFire();                // true once when quiet elapsed since last Signal
}
public sealed class RotatingLog : IDisposable {
    public RotatingLog(string path, long maxBytes);  // rotates to path + ".1"
    public void WriteLine(string line);              // prefixes "yyyy-MM-dd HH:mm:ss "
}
public sealed class FirewallStatus { public bool HasAllow; public bool HasBlock; public bool Ok => HasAllow && !HasBlock; }
public static class FirewallStatusParser { public static FirewallStatus Parse(string output); } // lines "ALLOW=n" "BLOCK=n"
public sealed class NetworkConnection { public int InterfaceIndex; public string Alias; public string Category; public bool IsPublic => Category == "Public"; }
public static class NetworkProfileParser { public static IReadOnlyList<NetworkConnection> Parse(string output); } // "idx|alias|category"
```
- [ ] Step 1: tests:
  - Parser: `connection request from MacBook Pro của Thịnh (Mac15,6) with deviceID = AA:BB` -> ClientConnecting, name `MacBook Pro của Thịnh`, model `Mac15,6`; `*** ERROR lost connection with client (network problem?)` -> ConnectionLost; lines starting `ERROR` or containing `failed to` -> Error; other -> None.
  - Tracker: connecting log -> connected with name; one zero TCP sample keeps connected; two -> disconnected; positive sample in between resets count; ConnectionLost -> disconnected immediately; TCP samples while not connected never connect.
  - RestartPolicy (fake clock): delays 1,2,5,10,30 s; 6th failure within 2 min -> null; failures spaced > 2 min apart never give up; ReportHealthy resets to 1 s.
  - Debouncer: Signal at t0, ShouldFire false at t0+4 s, true at t0+5 s, then false; a second Signal at t0+3 s pushes fire to t0+8 s.
  - RotatingLog: writing past maxBytes creates `.1` and restarts main file; works when directory does not exist yet.
  - Firewall parser: `ALLOW=1\nBLOCK=0` Ok; `ALLOW=0` not ok; `BLOCK=2` HasBlock; garbage -> all false.
  - Network parser: `12|Wi-Fi|Public\n5|Ethernet|Private` -> two entries; blank/garbage lines skipped.
- [ ] Step 2: FAIL. Step 3: implement. Step 4: PASS. Step 5: commit `feat(core): log parsing, connection tracking, restart policy`.

### Task 5: Native uxplay build and bundle scripts

**Files:** `native/uxplay.ref`, `native/gst-elements.txt`, `native/build-uxplay.sh`, `native/bundle-runtime.sh`, `native/verify-bundle.ps1`

- [ ] Step 1: `uxplay.ref`: `URL=https://github.com/FDH2/UxPlay.git` and `COMMIT=28ed70a9876fe3f5864479f4e0d07abe4678383a`.
- [ ] Step 2: `gst-elements.txt` (one per line, `?` prefix = optional, warn only): `appsrc queue typefind fakesink h264parse h265parse decodebin avdec_h264 avdec_h265 avdec_aac avdec_alac videoconvert videoscale videoflip d3d11videosink ?d3d12videosink ?d3d11h264dec ?d3d11h265dec audioconvert audioresample volume level autoaudiosink wasapisink ?wasapi2sink ?directsoundsink autovideosink`.
- [ ] Step 3: `build-uxplay.sh` (bash, `set -euo pipefail`, runs in MSYS2 UCRT64): read ref, `git init`/`fetch --depth 1 origin $COMMIT`/`checkout FETCH_HEAD` into `build/uxplay-src`, `cmake -S ... -B build/uxplay -G Ninja -DCMAKE_BUILD_TYPE=Release -DNO_MARCH_NATIVE=ON`, `cmake --build build/uxplay --parallel`, check `build/uxplay/uxplay.exe` exists.
- [ ] Step 4: `bundle-runtime.sh <exe> <outdir>`: for each element, `gst-inspect-1.0 <el>` and parse `Filename` to the plugin DLL (required missing => exit 1); copy to `out/lib/gstreamer-1.0`; copy `uxplay.exe`, `gst-inspect-1.0.exe`, `gst-launch-1.0.exe` to `out/bin`, `gst-plugin-scanner.exe` to `out/libexec/gstreamer-1.0`; DLL closure: `ldd` on every copied exe/dll, keep paths under `/ucrt64/`, copy to `out/bin`, iterate until no new files; write `out/VERSION.txt` (`UxPlay 1.74 (28ed70a)` + GStreamer version); copy upstream `LICENSE` to `out/LICENSE-uxplay.txt`.
- [ ] Step 5: `verify-bundle.ps1 -Bundle <dir>`: env PATH = `<bundle>\bin;$env:SystemRoot\System32;$env:SystemRoot`, `GST_PLUGIN_SYSTEM_PATH_1_0`, `GST_PLUGIN_SCANNER_1_0`, `GST_REGISTRY_1_0` (temp), remove `GST_PLUGIN_PATH*`; run `uxplay.exe -v` (must print `UxPlay version`); `gst-inspect-1.0.exe <el>` for each element, exit code 0 required (optional => warning); print bundle size.
- [ ] Step 6: commit `build: uxplay native build and bundling scripts` (verified in CI, Task 9).

### Task 6: WPF app shell, theme, icons, platform services

**Files:** `src/MacExtend.App/MacExtend.App.csproj` (net48, UseWPF, UseWindowsForms, LangVersion latest, ApplicationIcon), `App.xaml(.cs)`, `AppPaths.cs`, `Themes/*`, `Native/Win32.cs`, `Services/DisplayInfo.cs`, `ShortPath.cs`, `KeepAwake.cs`, `AutoStart.cs`, `PowerShellRunner.cs`, `Firewall.cs`, `NetworkProfile.cs`, `TcpConnections.cs`, `UxPlayHost.cs`; `tools/make_icons.py`, `src/MacExtend.App/Assets/*.ico`

**Interfaces (produces):**
```csharp
static class AppPaths { string InstallDir, UxPlayBin, UxPlayExe, PluginDir, PluginScanner, DataDir, LogDir, SettingsFile, KeyFile, RegisterFile, GstRegistry; }
static class DisplayInfo { DisplayMode GetPrimary(); }                         // ENUM_CURRENT_SETTINGS
static class ShortPath { string Resolve(string path); }                       // ASCII passthrough, else GetShortPathNameW (create file first)
static class KeepAwake { void Set(bool on); }                                  // must be called on UI thread
static class AutoStart { bool IsEnabled(); void Set(bool on); }               // HKCU\...\Run "MacExtend" = "\"exe\" --background"
static class PowerShellRunner { Task<(int code, string output)> RunAsync(string script, bool elevated); } // -EncodedCommand; elevated => runas, output via temp file
static class Firewall { Task<FirewallStatus> GetStatusAsync(); Task<bool> FixAsync(); }  // FixAsync elevated: remove Block rules for uxplay.exe, add Allow (Private,Domain)
static class NetworkProfile { Task<IReadOnlyList<NetworkConnection>> GetAsync(); Task<bool> MakePrivateAsync(int ifIndex); }
static class TcpConnections { int CountEstablished(int pid); }                 // IPv4 + IPv6
sealed class UxPlayHost : IDisposable {
    event Action<string> Output; event Action<int> Exited;
    int ProcessId { get; } bool IsRunning { get; }
    void Start(IReadOnlyList<string> args);   // hidden, UTF-8 stdout/stderr, clean env
    Task StopAsync(TimeSpan grace);           // AttachConsole + CTRL_C, then Kill
}
```
- [ ] Step 1: `tools/make_icons.py`: supersampled rasterizer drawing a rounded monitor glyph with a status dot; writes `app.ico` (16..256) and `tray-stopped/ready/connected/error.ico` (16,20,24,32,40,48). Run it, commit outputs.
- [ ] Step 2: project + App startup: parse `--background`; single instance via `Mutex("Local\\MacExtend.SingleInstance")` + `EventWaitHandle("Local\\MacExtend.Show")`; ThemeManager reads `AppsUseLightTheme`, swaps color dictionary, re-applies on `SystemEvents.UserPreferenceChanged`.
- [ ] Step 3: services as above. `UxPlayHost.StopAsync`: lock; `FreeConsole(); AttachConsole(pid); SetConsoleCtrlHandler(null,true); GenerateConsoleCtrlEvent(0,0); FreeConsole();` wait `grace`; `SetConsoleCtrlHandler(null,false)` in finally; Kill if still alive.
- [ ] Step 4: `dotnet build src/MacExtend.App -c Release` (locally on macOS if WPF targeting works, else CI). Commit `feat(app): shell, theme and platform services`.

### Task 7: ReceiverController

**Files:** `src/MacExtend.App/Services/ReceiverController.cs`

**Interfaces:**
```csharp
sealed class ReceiverController : IDisposable {
    ReceiverController(Func<AppSettings> settings);
    ReceiverPhase Phase { get; } string ClientName { get; } string LastError { get; }
    string EffectiveDeviceName { get; } RequestedVideo EffectiveVideo { get; }
    bool NeedsRestart { get; }          // current settings would produce different args than running ones
    event Action StateChanged;          // raised on UI thread
    void Start(); Task StopAsync(); Task RestartAsync();
}
```
- [ ] Step 1: Start: compute name/video/args, write args to log, `UxPlayHost.Start`, Phase=Starting, after 1.5 s alive -> Ready. Output -> RotatingLog + parser -> tracker -> Connected/Ready + KeepAwake.
- [ ] Step 2: 2 s DispatcherTimer: `TcpConnections.CountEstablished(pid)` -> tracker; Debouncer for `NetworkChange.NetworkAddressChanged` (5 s) -> restart; `SystemEvents.PowerModeChanged` Resume -> restart after 3 s; Suspend -> stop.
- [ ] Step 3: unexpected exit -> RestartPolicy delay or Phase=Failed with LastError (last error log line). Healthy after 30 s -> ReportHealthy.
- [ ] Step 4: build, commit `feat(app): receiver controller`.

### Task 8: UI (main window, illustration, tray)

Use the frontend-design skill for visual direction before writing XAML.

**Files:** `ViewModels/*.cs`, `Views/MainWindow.xaml(.cs)`, `Views/DeviceIllustration.xaml(.cs)`, `Tray/TrayIcon.cs`, `Tray/ModernMenuRenderer.cs`, `Themes/Controls.xaml`

- [ ] Step 1: Controls.xaml styles: window chrome buttons, primary/secondary/subtle buttons, ToggleSwitch (CheckBox template, animated thumb), segmented RadioButtons, rounded TextBox, Card border, section header text, status pill, ScrollViewer with thin scrollbar.
- [ ] Step 2: MainWindow 980x660, custom title bar (WindowChrome), Win11 rounded corners (DWMWA_WINDOW_CORNER_PREFERENCE), dark title via DWMWA_USE_IMMERSIVE_DARK_MODE. Left: hero (illustration Mac -> Windows with animated link per phase, status title/subtitle, client name, PIN digits, Start/Stop, 4-step guide). Right: cards Thiết bị, Hình ảnh, Âm thanh, Bảo mật, Hệ thống (startup, firewall row with Fix, network row with Make Private), footer (log folder, versions, GPL). Restart banner when `NeedsRestart`.
- [ ] Step 3: MainViewModel: two-way settings properties, auto-save debounced 500 ms, commands Start/Stop/Restart/NewPin/ForgetDevices/FixFirewall/MakePrivate/OpenLogs/CopyName; refresh firewall/network status on load and every 30 s while window visible.
- [ ] Step 4: Tray: NotifyIcon with state icons, tooltip, left-click opens window, menu (status header, Mở MacExtend, Bắt đầu/Dừng, Thoát) drawn by ModernMenuRenderer following theme. Closing window hides to tray (one-time balloon). Exit: `await controller.StopAsync()` then shutdown; `SessionEnding` also stops.
- [ ] Step 5: build, commit `feat(app): main window and tray`.

### Task 9: CI, installer, docs

**Files:** `.github/workflows/build.yml`, `installer/MacExtend.iss`, `README.md`, `docs/TESTING.md`, `LICENSE`

- [ ] Step 1: workflow jobs: `runtime` (windows-latest, msys2/setup-msys2@v2 UCRT64 with cmake ninja gcc pkgconf openssl libplist gstreamer gst-plugins-base/good/bad gst-libav, git; run build + bundle; `pwsh native/verify-bundle.ps1`; upload `uxplay-runtime`), `app` (needs runtime; setup-dotnet 10.0.x; `dotnet test`; `dotnet build -c Release`; assemble `stage/`; zip portable; `iscc` installer (choco install innosetup if missing); upload artifacts), `release` on tags `v*` (softprops/action-gh-release@v2 with both files).
- [ ] Step 2: Inno Setup: per-machine install to `{autopf}\MacExtend`, Start Menu shortcut, optional autostart task, firewall rule on install via PowerShell, removal on uninstall, close running app before upgrade (`AppMutex=Local\MacExtend.SingleInstance` is per-session, so also `CloseApplications=yes`).
- [ ] Step 3: README (Vietnamese: what it is, download, first run, Mac steps, troubleshooting, build, licenses/credits), TESTING.md (T1..T12 checklist), LICENSE (GPLv3 text).
- [ ] Step 4: validate workflow YAML parses; commit `ci: build, package and release`.

---

## Self-Review

- Coverage: Mac discovery (Tasks 5, 6, 9), extended display args (3), auto 1080p policy (2), PIN (2, 3, 8), audio toggle (3, 8), autostart (6, 8), firewall/public network (4, 6, 8, 9), sleep/wake/network/crash (4, 7), keep awake (6, 7), graceful stop (6), beautiful Vietnamese UI with no emoji (8), CI + installer (9).
- Types: `RequestedVideo`, `DisplayMode`, `FirewallStatus`, `NetworkConnection`, `ReceiverPhase` are defined in Core (Tasks 2 and 4) and only consumed by App.
- Deviation from the writing-plans template: Windows-only tasks (6 to 9) list exact interfaces and behaviors rather than full code, because they cannot run on the authoring machine. They are verified by compilation and CI.

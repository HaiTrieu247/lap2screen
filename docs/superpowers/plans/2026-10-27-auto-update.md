# MacExtend Auto-Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MacExtend checks GitHub for a newer build, downloads only the parts that changed and updates itself in place, so the user never uninstalls and reinstalls.

**Architecture:** Every push to `main` publishes a GitHub Release `v<prefix>.<run_number>` with an `update.ini` manifest, an app zip (about 1 MB) and a runtime zip (uxplay + GStreamer, changes rarely, identified by a fingerprint). The app reads `releases/latest/download/update.ini`, downloads and verifies (SHA-256) the needed zips into a staging folder, exits, and a helper copy of itself in %TEMP% applies the files (elevated through one UAC prompt when the install folder is not writable, e.g. Program Files), rolls back on failure and relaunches MacExtend.

**Tech Stack:** C# netstandard2.0 Core (xUnit tests on net10.0), WPF net48 app, HttpClient, System.IO.Compression, GitHub Actions, Inno Setup.

**Spec:** No written spec (user waived it again). Design approved in chat on 2026-10-27; restated in Global Constraints.

## Global Constraints

- Do NOT git commit, push, tag or merge. Leave all changes uncommitted (user rule).
- Repository for updates: `HaiTrieu247/lap2screen`, embedded at build time from `github.repository` (default constant for local builds).
- Manifest URL: `https://github.com/<repo>/releases/latest/download/update.ini`; asset URL: `https://github.com/<repo>/releases/download/v<version>/<file>`.
- Version: `version.txt` prefix (`1.0`) + `.` + `GITHUB_RUN_NUMBER` on main; tag `vX.Y.Z` builds use `X.Y.Z`. Versions compare on Major.Minor.Build only.
- Release on every push to `main` and every `v*` tag; never on pull requests.
- App zip always downloaded when a newer version exists; runtime zip only when `runtime.id` differs from `uxplay/RUNTIME_ID` installed (missing file = differs).
- Install location stays Program Files (user choice): applying needs one UAC prompt; declining UAC relaunches the old version with a message.
- Check 1 minute after start and every 6 hours when "Tự kiểm tra cập nhật" is on (default on); manual "Kiểm tra cập nhật" button always available. Never install without the user clicking "Cập nhật".
- A failed download or hash mismatch never touches the installed files; a failed apply restores the previous files.
- Vietnamese UI text, no emoji.

## Review Focus

1. Update applied while the install folder is partially locked (antivirus, Explorer preview) -> must retry, then roll back completely, never leave a half-updated install. Test: `UpdateApplierTests.RollsBackWhenAFileCannotBeReplaced`.
2. Malicious or corrupt zip with `..\` entries -> must not write outside the staging folder. Test: `UpdateStagerTests.RejectsEntriesOutsideTarget`.
3. Assembly version 1.0.60.0 vs manifest 1.0.60 -> must not offer the same version as an update. Test: `AppVersionTests`.
4. User clicks No on UAC -> old version restarts and says the update was cancelled; staging is kept for retry. Covered by helper flow, manual test U4.
5. GitHub unreachable / 404 before the first release exists -> automatic checks stay silent, manual check shows a clear message. Test: `UpdateManifestTests.GarbageIsInvalid` plus manual test U1.

---

### Task U1: Core — version, manifest, decision, settings flag

**Files:** Create `src/MacExtend.Core/Updates/AppVersion.cs`, `UpdateManifest.cs`, `UpdateDecision.cs`; modify `AppSettings.cs`, `SettingsStore.cs`; tests `tests/MacExtend.Core.Tests/Updates/*.cs`, `SettingsStoreTests.cs`.

**Interfaces:**
```csharp
public static class AppVersion { public static Version Normalize(Version v); public static bool TryParse(string s, out Version v); }
public sealed class UpdatePackage { public string File; public string Sha256; public long Size; }      // get-only, ctor
public sealed class UpdateManifest {
    public Version Version { get; } public UpdatePackage App { get; } public UpdatePackage Runtime { get; } public string RuntimeId { get; }
    public static UpdateManifest Parse(string text);   // null when version or app package missing/invalid
    public static string Serialize(UpdateManifest m);
}
public sealed class UpdateDecision {
    public bool Available { get; } public bool NeedsRuntime { get; } public long DownloadBytes { get; }
    public static UpdateDecision For(Version current, string installedRuntimeId, UpdateManifest manifest);
}
// AppSettings.AutoCheckUpdates (bool, default true), persisted as "AutoCheckUpdates"
```
- [ ] Tests first: normalize 1.0.60.0 == 1.0.60; TryParse("1.0.60"), ("v1.0.60") ok, ("abc") false; manifest round-trip; missing version/app -> null; sha256 must be 64 hex chars; runtime optional; decision: newer -> Available; same/older -> not; runtime id equal -> no runtime; empty installed id -> runtime; DownloadBytes sums; AutoCheckUpdates round-trip + default true.
- [ ] Run (RED), implement, run (GREEN).

### Task U2: Core — staging and applying files

**Files:** Create `src/MacExtend.Core/Updates/UpdateStager.cs`, `UpdateApplier.cs`, `FileHash.cs`; tests.

**Interfaces:**
```csharp
public static class FileHash { public static string Sha256(string path); }                       // lowercase hex
public static class UpdateStager { public static void Extract(string zipPath, string targetDir); } // throws InvalidDataException on zip-slip
public static class UpdateApplier {
    // staging/app/** -> installDir/**; staging/runtime/** replaces installDir/uxplay when present
    public static void Apply(string stagingDir, string installDir, Action<string> log);         // rolls back and rethrows on failure
    public static void CleanupLeftovers(string installDir);                                     // removes *.old and uxplay.old
}
```
- [ ] Tests first: hash of known content; extract normal zip; zip with `../evil.txt` throws and writes nothing outside; apply copies new files and keeps unrelated files; apply with runtime replaces uxplay folder entirely; failure (a directory where a file must go) restores every original file and the old uxplay; cleanup removes leftovers.
- [ ] RED, implement (retry IO 3x 300 ms, rename-aside then copy, delete backups after success), GREEN.

### Task U3: App — update service, helper process, UI

**Files:** Create `src/MacExtend.App/Services/UpdateService.cs`, `Services/UpdateHelper.cs`; modify `App.xaml.cs`, `MacExtend.App.csproj` (AssemblyMetadata UpdateRepository), `ViewModels/MainViewModel.cs`, `Views/MainWindow.xaml`, `Tray/TrayIcon.cs`, `AppPaths.cs`.

- UpdateService: `CheckAsync(bool manual)`, `DownloadAsync(IProgress<double>)` -> staging dir `%LOCALAPPDATA%\MacExtend\updates\<version>` with `update.ini`, `app/`, optional `runtime/`; verifies SHA-256 before extracting; `LaunchApplyAndExit()` copies MacExtend.exe/.config/MacExtend.Core.dll to `%TEMP%\MacExtend-update-<guid>` and starts `--update-run <staging> <installDir> <pid>`.
- UpdateHelper (`--update-run`): wait for pid, apply in-process if install dir writable else run itself elevated with `--update-apply <staging> <installDir> <result>`; elevated mode also sets Inno `DisplayVersion`; relaunch `<installDir>\MacExtend.exe --after-update <version>` or `--update-cancelled` / `--update-failed`.
- App startup: dispatch helper modes before single-instance; `CleanupLeftovers`; show message after update.
- UI: banner (Available / Downloading %), footer "Kiểm tra cập nhật" + status, toggle "Tự kiểm tra cập nhật" in Hệ thống; tray item "Cập nhật lên X" and one balloon per new version.
- [ ] Build app on macOS (compile check); Core tests still green.

### Task U4: CI, installer, docs

**Files:** `version.txt`, `.github/workflows/build.yml`, `native/bundle-runtime.sh` (RUNTIME_ID), `installer/MacExtend.iss` ([UninstallDelete]), `README.md`, `docs/TESTING.md` (U1..U6).
- [ ] bundle-runtime.sh writes `RUNTIME_ID` = sha256 of (uxplay.ref, gst-elements.txt, bundle-runtime.sh, `pacman -Q` of mingw packages).
- [ ] workflow: version from version.txt + run number; app job builds app zip, runtime zip, update.ini; release job on push to main or v* tags publishes all five assets with `make_latest`.
- [ ] Validate YAML (ruby), `bash -n`; simulate bundle script again.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.Core.Updates;
using MacExtend.Native;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>The CoreDevice runtime under {app}\coredevice: what is installed, getting this build's zip, installing it.</summary>
    internal static class CoreDeviceRuntime
    {
        public const string NoMetadataMessage = "Bản build cục bộ không có thông tin runtime. Hãy dùng bản do CI build.";

        private const int ErrorCancelled = 1223;

        /// <summary>The zip this build was made with (CI metadata); null for a local build.</summary>
        public static RuntimePackage Package { get; } = RuntimePackage.FromMetadata(key =>
            typeof(CoreDeviceRuntime).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value);

        /// <summary>
        /// No Inno Setup uninstaller beside MacExtend.exe: a copy the user unpacked somewhere. It installs the runtime in
        /// its own folder without UAC and never gets the Scheduled Task; Wi-Fi asks UAC per session. The same answer in an
        /// elevated process, which can write Program Files too. An installed copy elevates once for its setup.
        /// </summary>
        public static bool IsPortable
        {
            get
            {
                try
                {
                    return !InstallLayout.IsInstalledCopy(Directory.GetFiles(AppPaths.InstallDir, "unins*.exe"));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // a folder that cannot even be listed never gets a task
                    return true;
                }
            }
        }

        /// <summary>
        /// Installed and inside the 64-bit Program Files folder (InstallLayout.AllowsTask), so only administrators can
        /// change its files: the Scheduled Task may run its pythonw.exe, and the elevated setup may stage the runtime
        /// there. Anywhere else the copy is treated like a portable one. The elevated helper reads this itself; it never
        /// takes the caller's word for it.
        /// </summary>
        public static bool TaskAllowed => InstallLayout.AllowsTask(!IsPortable, Path.GetFullPath(AppPaths.InstallDir),
                                                                   Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

        /// <summary>
        /// Wi-Fi starts tunneld with the task: <see cref="TaskAllowed"/> and an account that elevates by itself (a
        /// standard account's "highest privileges" are not an administrator's). Otherwise every Wi-Fi session asks UAC.
        /// WindowsSetupState.UseTask (E1) is this.
        /// </summary>
        public static bool UsesTask => TaskAllowed && Elevation.IsAdminAccount;

        public static string InstalledId
        {
            get
            {
                try
                {
                    return File.Exists(AppPaths.CoreDeviceRuntimeIdFile) ? File.ReadAllText(AppPaths.CoreDeviceRuntimeIdFile).Trim() : null;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }

        public static bool IsInstalled => File.Exists(AppPaths.CoreDevicePython) && !string.IsNullOrEmpty(InstalledId);

        /// <summary>Installed and the one this build was made with (an older one is replaced by the updater or by setup).</summary>
        public static bool IsCurrent => IsInstalled && (Package == null || InstalledId == Package.Id);

        private static string DownloadDir => Path.Combine(AppPaths.UpdatesDir, "coredevice");

        /// <summary>Downloads this build's runtime zip from its own release into %LOCALAPPDATA%\MacExtend\updates\coredevice.</summary>
        /// <param name="progress">Fraction 0..1 of the zip.</param>
        /// <returns>The zip, its SHA-256 checked.</returns>
        public static async Task<string> DownloadAsync(IProgress<double> progress, CancellationToken cancel)
        {
            var package = Package ?? throw new InvalidOperationException(NoMetadataMessage);
            Directory.CreateDirectory(DownloadDir);
            var zip = Path.Combine(DownloadDir, package.File);
            await UpdateService.DownloadVerifiedAsync(UpdateService.ReleaseAssetUrl(UpdateService.CurrentLabel, package.File), zip, package.Sha256,
                                                      new Fraction(progress, package.Size), cancel,
                                                      "Bản phát hành này không có gói runtime trên GitHub. Nếu đây là bản beta từ CI, chọn tệp "
                                                      + package.File + " trong artifact.");
            return zip;
        }

        /// <summary>A zip the user picked (from a CI artifact): only this build's own package is accepted.</summary>
        /// <exception cref="InvalidDataException">Another file.</exception>
        public static void CheckZip(string path)
        {
            var package = Package ?? throw new InvalidOperationException(NoMetadataMessage);
            if (!package.Matches(FileHash.Sha256(path))) throw new InvalidDataException("Tệp này không phải gói runtime của bản MacExtend đang chạy.");
        }

        /// <summary>
        /// The one-time setup. Installed copy inside Program Files (<see cref="TaskAllowed"/>): one UAC for the runtime
        /// (when <paramref name="zip"/> is given), the firewall rules and, when <see cref="UsesTask"/>, the task. Any other
        /// copy: the runtime is installed here, without UAC and without rules (<see cref="AddFirewallRulesAsync"/> makes
        /// them before its first Wi-Fi session).
        /// </summary>
        /// <param name="zip">A zip from <see cref="DownloadAsync"/> or <see cref="CheckZip"/>; null keeps the installed runtime.</param>
        /// <returns>"ok", "cancelled" (UAC declined) or "failed: ...".</returns>
        public static Task<string> SetupAsync(string zip) => Task.Run(() =>
        {
            using (var log = new RotatingLog(AppPaths.CoreDeviceLog, 512 * 1024))
            {
                // only a copy in a folder that only administrators can change is set up elevated; any other copy, even an
                // installed one, is treated like a portable copy
                var outcome = TaskAllowed ? SetupElevated(zip, log) : InstallHere(zip, log);
                log.WriteLine("[setup] outcome: " + outcome);
                if (outcome == SetupHelperResult.Ok && zip != null) ForgetDownload(zip);
                return outcome;
            }
        });

        private static string InstallHere(string zip, RotatingLog log)
        {
            if (zip == null) return SetupHelperResult.Ok;
            if (Package == null) return "failed: " + NoMetadataMessage;
            try
            {
                RuntimeInstaller.Install(zip, Package, AppPaths.CoreDeviceRoot, line => log.WriteLine("[setup] " + line));
                return SetupHelperResult.Ok;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return "failed: " + e.Message;
            }
        }

        /// <summary>
        /// Only the four firewall rules, with one UAC: for a copy whose setup made none (portable, or installed outside
        /// Program Files), before its first Wi-Fi session. No runtime is staged and no task is made; running it again changes nothing.
        /// </summary>
        /// <returns>"ok", "cancelled" (UAC declined) or "failed: ...".</returns>
        public static Task<string> AddFirewallRulesAsync() => Task.Run(() =>
        {
            using (var log = new RotatingLog(AppPaths.CoreDeviceLog, 512 * 1024))
            {
                var outcome = RunElevated(result => new[] { CoreDeviceHelpers.RulesArg, result }, log);
                log.WriteLine("[setup] firewall rules outcome: " + outcome);
                return outcome;
            }
        });

        private static string SetupElevated(string zip, RotatingLog log)
        {
            // the elevated side refuses (exit 2) a network or device path; say why instead of asking UAC for nothing
            if (zip != null && !HelperPaths.IsLocalDrivePath(zip))
            {
                return "failed: Tệp runtime phải nằm trên ổ đĩa của máy này. Hãy chép tệp về máy rồi chọn lại.";
            }
            return RunElevated(result => new[]
            {
                CoreDeviceHelpers.SetupArg, zip ?? CoreDeviceHelpers.NoZip, result, Elevation.CurrentUserName(),
                UsesTask ? "1" : "0",
            }, log);
        }

        /// <summary>Runs one of this exe's elevated helper modes behind UAC and logs the lines of its result file.</summary>
        /// <param name="args">The mode's arguments, given the path of a result file that does not exist yet.</param>
        private static string RunElevated(Func<string, string[]> args, RotatingLog log)
        {
            var result = Path.Combine(ResultDir(), HelperPaths.ResultFileName(Guid.NewGuid()));
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(AppPaths.SelfExe, WindowsCommandLine.Join(args(result)))
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                }))
                {
                    p?.WaitForExit();
                }
                var parsed = SetupHelperResult.Parse(File.Exists(result) ? File.ReadAllText(result) : null);
                foreach (var line in parsed.Lines) log.WriteLine("[setup] " + line);
                if (parsed.Succeeded) return SetupHelperResult.Ok;
                if (parsed.Outcome == null) return "failed: Bước cài đặt có quyền quản trị không trả kết quả.";
                return parsed.Outcome.StartsWith("failed: ", StringComparison.Ordinal) ? parsed.Outcome : "failed: " + parsed.Outcome;
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
            {
                return "cancelled";
            }
            catch (Exception e) when (e is Win32Exception || e is IOException || e is UnauthorizedAccessException)
            {
                return "failed: " + e.Message;
            }
            finally
            {
                TryDelete(result);
            }
        }

        /// <summary>The user's own temp folder, or the updates folder when TEMP is not a local drive path (the helper refuses those).</summary>
        private static string ResultDir()
        {
            var temp = Path.GetTempPath();
            if (HelperPaths.IsLocalDrivePath(temp)) return temp;
            Directory.CreateDirectory(AppPaths.UpdatesDir);
            return AppPaths.UpdatesDir;
        }

        /// <summary>The downloaded zip has done its job once installed; a zip the user picked stays where it is.</summary>
        private static void ForgetDownload(string zip)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(zip)) ?? "";
            if (string.Equals(dir.TrimEnd('\\'), DownloadDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) TryDelete(zip);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // %TEMP% and the updates folder are cleaned later
            }
        }

        /// <summary>Bytes to a fraction, passed straight on so the reports keep their order.</summary>
        private sealed class Fraction : IProgress<long>
        {
            private readonly IProgress<double> _target;
            private readonly long _total;

            public Fraction(IProgress<double> target, long total)
            {
                _target = target;
                _total = Math.Max(1, total);
            }

            public void Report(long value) => _target?.Report(Math.Min(1.0, value / (double)_total));
        }
    }
}

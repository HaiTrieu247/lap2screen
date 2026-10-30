using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.Core.Updates;
using Microsoft.Win32;

namespace MacExtend.Services
{
    /// <summary>
    /// The installed files cannot be replaced while MacExtend runs from them, so a copy of
    /// MacExtend.exe in %TEMP% does the work in two modes:
    ///   --update-run  staging install pid    (as the user) waits for MacExtend to exit, applies the
    ///                                         update (elevating once when the folder needs it) and
    ///                                         starts MacExtend again
    ///   --update-apply staging install result (elevated) only copies the files
    /// </summary>
    internal static class UpdateHelper
    {
        public const string AfterUpdateArg = "--after-update";
        public const string CancelledArg = "--update-cancelled";
        public const string FailedArg = "--update-failed";

        private const string RunArg = "--update-run";
        private const string ApplyArg = "--update-apply";
        private const int ErrorCancelled = 1223;
        // Inno Setup AppId from installer/MacExtend.iss
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F1C2B9E-3D4A-4E57-9C61-2A8B7D5E4F10}_is1";
        // App.OnStartup reaches CoreDeviceHelpers (CtrlCHelper) before this helper's own mode, so the copy needs that DLL too
        private static readonly string[] HelperFiles = { "MacExtend.exe", "MacExtend.exe.config", "MacExtend.Core.dll", "MacExtend.CoreDeviceHost.dll" };

        /// <summary>Applying into this folder needs administrator rights (Program Files).</summary>
        public static bool NeedsElevation(string installDir) => !CanWrite(installDir);

        /// <summary>Starts the helper; the caller exits right after so the helper can replace the files.</summary>
        public static void Launch(string stagingDir)
        {
            var helperDir = Path.Combine(Path.GetTempPath(), "MacExtend-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(helperDir);
            foreach (var name in HelperFiles)
            {
                var source = Path.Combine(AppPaths.InstallDir, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(helperDir, name));
            }
            var args = new[]
            {
                RunArg, stagingDir, AppPaths.InstallDir.TrimEnd('\\'),
                Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture),
            };
            Process.Start(new ProcessStartInfo(Path.Combine(helperDir, "MacExtend.exe"), WindowsCommandLine.Join(args))
            {
                UseShellExecute = false,
                WorkingDirectory = helperDir,
            });
        }

        /// <summary>Runs a helper mode when the arguments ask for one.</summary>
        /// <returns>true when this process was a helper and should exit with <paramref name="exitCode"/>.</returns>
        public static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = 0;
            if (args.Length == 4 && args[0] == RunArg)
            {
                exitCode = Run(args[1], args[2], args[3]);
                return true;
            }
            if (args.Length == 4 && args[0] == ApplyArg)
            {
                exitCode = ApplyElevated(args[1], args[2], args[3]);
                return true;
            }
            return false;
        }

        private static int Run(string staging, string installDir, string pidText)
        {
            using (var log = new RotatingLog(AppPaths.UpdateLog, 512 * 1024))
            {
                var version = StagedVersion(staging);
                log.WriteLine("[update] applying " + version + " to " + installDir);
                WaitForExit(pidText, log);

                string outcome;
                if (CanWrite(installDir))
                {
                    outcome = Apply(staging, installDir, version, log);
                }
                else
                {
                    var resultFile = Path.Combine(Path.GetTempPath(), "MacExtend-update-" + Guid.NewGuid().ToString("N") + ".result");
                    var self = Process.GetCurrentProcess().MainModule.FileName;
                    try
                    {
                        using (var elevated = Process.Start(new ProcessStartInfo(self, WindowsCommandLine.Join(new[] { ApplyArg, staging, installDir, resultFile }))
                        {
                            UseShellExecute = true,
                            Verb = "runas",
                            WindowStyle = ProcessWindowStyle.Hidden,
                        }))
                        {
                            elevated.WaitForExit();
                        }
                        outcome = File.Exists(resultFile) ? File.ReadAllText(resultFile).Trim() : "failed: no result from the elevated updater";
                    }
                    catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
                    {
                        outcome = "cancelled";
                    }
                    finally
                    {
                        TryDelete(resultFile);
                    }
                }

                log.WriteLine("[update] outcome: " + outcome);
                var relaunchArgs = outcome == "ok" ? new[] { AfterUpdateArg, version }
                    : outcome == "cancelled" ? new[] { CancelledArg }
                    : new[] { FailedArg };
                Process.Start(new ProcessStartInfo(Path.Combine(installDir, "MacExtend.exe"), WindowsCommandLine.Join(relaunchArgs))
                {
                    UseShellExecute = false,
                    WorkingDirectory = installDir,
                });
                return outcome == "ok" ? 0 : 1;
            }
        }

        private static int ApplyElevated(string staging, string installDir, string resultFile)
        {
            using (var log = new RotatingLog(AppPaths.UpdateLog, 512 * 1024))
            {
                var outcome = Apply(staging, installDir, StagedVersion(staging), log);
                try
                {
                    File.WriteAllText(resultFile, outcome);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    log.WriteLine("[update] could not write result: " + e.Message);
                }
                return outcome == "ok" ? 0 : 1;
            }
        }

        private static string Apply(string staging, string installDir, string version, RotatingLog log)
        {
            try
            {
                if (CoreDeviceUpdate.IsStaged(staging)) EndCoreDeviceUsers(installDir, log);
                UpdateApplier.Apply(staging, installDir, line => log.WriteLine("[update] " + line));
                SetInstalledVersion(version);
                return "ok";
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return "failed: " + e.Message;
            }
        }

        /// <summary>
        /// tunneld (the Scheduled Task's pythonw.exe) and a Wi-Fi session's serve-web python.exe keep the CoreDevice runtime's files
        /// open; end them before the applier moves coredevice aside. A copy without a task (portable) has nothing to stop; this
        /// never fails the update, which retries the move and rolls back if a file stays locked.
        /// </summary>
        private static void EndCoreDeviceUsers(string installDir, RotatingLog log)
        {
            var stopped = PowerShellRunner.RunAsync(CoreDeviceSetupScripts.StopTask()).GetAwaiter().GetResult().Succeeded;
            log.WriteLine("[update] tunneld task stop: " + (stopped ? "ok" : "not stopped"));
            var runtimeDir = Path.Combine(installDir, "coredevice");
            var ended = 0;
            foreach (var name in new[] { "pythonw", "python" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        try
                        {
                            if (!CoreDeviceUpdate.IsInsideRuntime(p.MainModule.FileName, runtimeDir)) continue;
                            p.Kill();
                            p.WaitForExit(5000);
                            ended++;
                        }
                        catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is NotSupportedException)
                        {
                            // exited meanwhile, or not ours to inspect
                        }
                    }
                }
            }
            log.WriteLine("[update] runtime programs ended: " + ended);
        }

        /// <summary>Keeps the version shown in Windows "Installed apps" in step with the files.</summary>
        private static void SetInstalledVersion(string version)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = root.OpenSubKey(UninstallKey, true))
                {
                    key?.SetValue("DisplayVersion", version);
                    key?.SetValue("DisplayName", "MacExtend " + version);
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is System.Security.SecurityException || e is IOException)
            {
                // portable copy or not elevated: nothing registered to update
            }
        }

        private static void WaitForExit(string pidText, RotatingLog log)
        {
            if (!int.TryParse(pidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)) return;
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    if (!p.WaitForExit(30000)) log.WriteLine("[update] MacExtend did not exit within 30 s; continuing");
                }
            }
            catch (ArgumentException)
            {
                // already exited
            }
            // uxplay ends with MacExtend through the job object; give Windows a moment to release its files
            System.Threading.Thread.Sleep(1000);
        }

        private static string StagedVersion(string staging)
        {
            try
            {
                var manifest = UpdateManifest.Parse(File.ReadAllText(Path.Combine(staging, "update.ini")));
                return manifest?.Version.ToString(3) ?? "?";
            }
            catch (IOException)
            {
                return "?";
            }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is IOException || e is System.Security.SecurityException)
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}

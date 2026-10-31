using System;
using System.IO;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;
using MacExtend.Native;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>
    /// MacExtend.exe's helper modes for "Điều khiển trực tiếp", handled before any UI or the single-instance check:
    ///   signal-ctrl-c PID                         one Ctrl+C to serve-web's console
    ///   --coredevice-setup ZIP|- USER TASK(0|1)   elevated, one UAC: this build's runtime (ZIP, a local drive path; "-"
    ///                                             keeps the installed one), the firewall rules and, for TASK=1, the
    ///                                             Scheduled Task. The runtime and the task only for an installed copy
    ///                                             inside Program Files or Program Files (x86), checked here again;
    ///                                             otherwise any old task is removed.
    ///   --coredevice-rules                        elevated, one UAC: only the firewall rules, for a copy whose setup made
    ///                                             none, before its first Wi-Fi session; no runtime, no task; running it
    ///                                             again gives the same rules
    ///   --coredevice-remove                       elevated: task and rules (uninstaller, CI)
    /// The elevated modes report only their exit code (HelperOutcome; the app shows HelperOutcomes.Message) and write
    /// nothing outside the install folder: no log, no result file, so no caller-chosen path ever receives their text.
    /// tunneld is never hosted here: the task starts the runtime's pythonw.exe itself, because an elevated .NET process
    /// would honour COR_PROFILER from the user's own environment.
    /// Known limitation: the setup helper itself is this .NET exe self-elevated through runas, so COR_PROFILER and
    /// COR_ENABLE_PROFILING from the user's environment can still load a profiler into the elevated setup process. This
    /// is inherent to any runas of a managed exe and falls in the "UAC is not a security boundary" class; a native
    /// elevation shim is a possible future follow-up.
    /// </summary>
    internal static class CoreDeviceHelpers
    {
        public const string SetupArg = "--coredevice-setup";
        public const string RulesArg = "--coredevice-rules";
        public const string RemoveArg = "--coredevice-remove";
        /// <summary>The ZIP argument when the runtime is already in place: firewall rules and task only.</summary>
        public const string NoZip = "-";

        /// <returns>true when <paramref name="args"/> asked for a helper mode; the process exits with <paramref name="exitCode"/>.</returns>
        public static bool TryRun(string[] args, out int exitCode)
        {
            args = args ?? new string[0];
            if (CtrlCHelper.TryRun(args, out exitCode)) return true;
            var mode = args.Length > 0 ? args[0] : null;
            HelperOutcome outcome;
            if (mode == SetupArg)
            {
                var valid = args.Length == 4 && (args[1] == NoZip || HelperPaths.IsLocalDrivePath(args[1])) && (args[3] == "0" || args[3] == "1");
                outcome = valid ? Run(() => Setup(args[1] == NoZip ? null : args[1], args[2], args[3] == "1")) : HelperOutcome.BadArguments;
            }
            else if (mode == RulesArg)
            {
                outcome = args.Length == 1 ? Run(Rules) : HelperOutcome.BadArguments;
            }
            else if (mode == RemoveArg)
            {
                outcome = args.Length == 1 ? Remove() : HelperOutcome.BadArguments;
            }
            else
            {
                exitCode = 0;
                return false;
            }
            exitCode = (int)outcome;
            return true;
        }

        private static HelperOutcome Run(Action steps)
        {
            try
            {
                steps();
                return HelperOutcome.Ok;
            }
            catch (StepFailed e)
            {
                return e.Outcome;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is InvalidDataException
                                      || e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException)
            {
                return HelperOutcome.Failed;
            }
        }

        /// <param name="zip">The runtime zip to install, or null to keep the installed one.</param>
        /// <param name="userName">The logged-on user (read by the unelevated app): the task's principal.</param>
        /// <param name="withTask">The caller's account elevates by itself, so a "highest privileges" task gives tunneld its rights.</param>
        private static void Setup(string zip, string userName, bool withTask)
        {
            RequireElevated();
            if (zip != null)
            {
                // the private copy, TARGET.new and TARGET.old are written and deleted with administrator rights: only in a
                // folder that only administrators can change (any other copy installs its runtime without UAC)
                if (!CoreDeviceRuntime.TaskAllowed) throw new StepFailed(HelperOutcome.NotProtectedLocation);
                // the package this exe was built with; the installer hashes its private copy and extracts only from it
                var expected = CoreDeviceRuntime.Package ?? throw new StepFailed(HelperOutcome.PackageMissing);
                using (var source = OpenZip(zip))
                {
                    // the task's tunneld (pythonw.exe) holds the runtime's files while it runs
                    TryScript(CoreDeviceSetupScripts.StopTask);
                    Install(source, expected);
                }
            }
            else
            {
                RequireRuntime();
            }

            AddRules();
            // decided again here, never taken from the unelevated caller: a task with highest privileges only runs a
            // pythonw.exe that only administrators can replace
            var task = withTask && CoreDeviceRuntime.TaskAllowed;
            if (task)
            {
                RunScript(() => CoreDeviceSetupScripts.RegisterTask(AppPaths.CoreDevicePythonw, AppPaths.CoreDevicePythonDir, userName),
                          HelperOutcome.TaskFailed);
            }
            else
            {
                // an older task may point at a folder that is no longer protected
                TryScript(CoreDeviceSetupScripts.RemoveTask);
            }
            Verify(task);
        }

        /// <summary>Only the firewall rules (AddFirewallRules removes the group first, so a second run is the same as one).</summary>
        private static void Rules()
        {
            RequireElevated();
            RequireRuntime();
            AddRules();
            Verify(false);
        }

        private static HelperOutcome Remove()
        {
            // tunneld first: the uninstaller deletes the runtime right after this
            var ok = TryScript(CoreDeviceSetupScripts.StopTask);
            ok &= TryScript(CoreDeviceSetupScripts.RemoveTask);
            ok &= TryScript(CoreDeviceSetupScripts.RemoveFirewallRules);
            return ok ? HelperOutcome.Ok : HelperOutcome.RemoveFailed;
        }

        private static void RequireElevated()
        {
            if (!Elevation.IsElevated) throw new StepFailed(HelperOutcome.NotElevated);
        }

        private static void RequireRuntime()
        {
            if (!File.Exists(AppPaths.CoreDevicePython) || !File.Exists(AppPaths.CoreDevicePythonw)) throw new StepFailed(HelperOutcome.RuntimeMissing);
        }

        private static FileStream OpenZip(string zip)
        {
            try
            {
                // other programs cannot change, rename or delete it while it is copied
                return new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException
                                      || e is System.Security.SecurityException)
            {
                throw new StepFailed(HelperOutcome.ZipNotFound);
            }
        }

        private static void Install(Stream source, RuntimePackage expected)
        {
            try
            {
                RuntimeInstaller.Install(source, expected, AppPaths.CoreDeviceRoot, null);
            }
            catch (RuntimePackageException e)
            {
                throw new StepFailed(HelperOutcomes.For(e.Problem));
            }
            catch (InvalidDataException)
            {
                // the right bytes, but an entry outside its folder or a zip that cannot be read
                throw new StepFailed(HelperOutcome.BadPackage);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // files in use or disk full; the installer has put the previous runtime back
                throw new StepFailed(HelperOutcome.InstallFailed);
            }
        }

        private static void AddRules() =>
            RunScript(() => CoreDeviceSetupScripts.AddFirewallRules(AppPaths.CoreDevicePythonDir), HelperOutcome.FirewallFailed);

        /// <summary>What the scripts made must be visible afterwards: all firewall rules, and the task when one was wanted.</summary>
        private static void Verify(bool needsTask)
        {
            var result = PowerShellRunner.RunAsync(CoreDeviceSetupScripts.Query()).GetAwaiter().GetResult();
            if (!CoreDeviceSetupScripts.ParseQuery(result.Output).IsComplete(needsTask)) throw new StepFailed(HelperOutcome.VerifyFailed);
        }

        /// <param name="script">Builds the script; a value it refuses (a '%' in the task definition) is that step's failure too.</param>
        private static void RunScript(Func<string> script, HelperOutcome failure)
        {
            string text;
            try
            {
                text = script();
            }
            catch (ArgumentException)
            {
                throw new StepFailed(failure);
            }
            if (!PowerShellRunner.RunAsync(text).GetAwaiter().GetResult().Succeeded) throw new StepFailed(failure);
        }

        private static bool TryScript(Func<string> script)
        {
            try
            {
                RunScript(script, HelperOutcome.Failed);
                return true;
            }
            catch (StepFailed)
            {
                return false;
            }
        }

        /// <summary>Ends the steps of a mode with its exit code.</summary>
        private sealed class StepFailed : Exception
        {
            public StepFailed(HelperOutcome outcome) : base(outcome.ToString())
            {
                Outcome = outcome;
            }

            public HelperOutcome Outcome { get; }
        }
    }
}

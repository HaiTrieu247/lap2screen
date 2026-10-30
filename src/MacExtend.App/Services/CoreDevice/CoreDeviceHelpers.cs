using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;
using MacExtend.Native;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>
    /// MacExtend.exe's helper modes for "Điều khiển trực tiếp", handled before any UI or the single-instance check:
    ///   signal-ctrl-c PID                                one Ctrl+C to serve-web's console
    ///   --coredevice-setup ZIP|- RESULT USER TASK(0|1)   elevated, one UAC: this build's runtime (ZIP; "-" keeps the
    ///                                                    installed one), the firewall rules and, for TASK=1, the
    ///                                                    Scheduled Task (only for an installed copy inside Program
    ///                                                    Files, checked here again; otherwise any old task is removed)
    ///   --coredevice-rules RESULT                        elevated, one UAC: only the firewall rules, for a copy without
    ///                                                    the task before its first Wi-Fi session; no runtime, no task;
    ///                                                    running it again gives the same rules
    ///   --coredevice-remove                              elevated: task and rules (uninstaller, CI)
    /// tunneld is never hosted here: the task starts the runtime's pythonw.exe itself, because an elevated .NET process
    /// would honour COR_PROFILER from the user's own environment. The elevated modes write nothing into the user's
    /// folders except the new RESULT file; the app logs the step lines it carries (SetupHelperResult).
    /// </summary>
    internal static class CoreDeviceHelpers
    {
        public const string SetupArg = "--coredevice-setup";
        public const string RulesArg = "--coredevice-rules";
        public const string RemoveArg = "--coredevice-remove";
        /// <summary>The ZIP argument when the runtime is already in place: firewall rules and task only.</summary>
        public const string NoZip = "-";
        /// <summary>Exit code of a helper mode given the wrong arguments; the app itself never starts from them.</summary>
        public const int BadArguments = 2;

        /// <returns>true when <paramref name="args"/> asked for a helper mode; the process exits with <paramref name="exitCode"/>.</returns>
        public static bool TryRun(string[] args, out int exitCode)
        {
            args = args ?? new string[0];
            if (CtrlCHelper.TryRun(args, out exitCode)) return true;
            if (args.Length > 0 && args[0] == SetupArg)
            {
                var valid = args.Length == 5 && (args[1] == NoZip || CoreDeviceOptions.IsWindowsAbsolutePath(args[1]))
                            && CoreDeviceOptions.IsWindowsAbsolutePath(args[2]) && (args[4] == "0" || args[4] == "1");
                exitCode = valid ? Setup(args[1] == NoZip ? null : args[1], args[2], args[3], args[4] == "1") : BadArguments;
                return true;
            }
            if (args.Length > 0 && args[0] == RulesArg)
            {
                exitCode = args.Length == 2 && CoreDeviceOptions.IsWindowsAbsolutePath(args[1]) ? Rules(args[1]) : BadArguments;
                return true;
            }
            if (args.Length > 0 && args[0] == RemoveArg)
            {
                exitCode = args.Length == 1 ? Remove() : BadArguments;
                return true;
            }
            exitCode = 0;
            return false;
        }

        /// <param name="zip">The runtime zip to install, or null to keep the installed one.</param>
        /// <param name="userName">The logged-on user (read by the unelevated app): the task's principal.</param>
        /// <param name="withTask">The caller's account elevates by itself, so a "highest privileges" task gives tunneld its rights.</param>
        private static int Setup(string zip, string resultFile, string userName, bool withTask) => Steps(resultFile, log =>
        {
            RequireElevated();
            if (zip != null)
            {
                // the package this exe was built with; the installer checks the hash on the handle it extracts from
                var expected = CoreDeviceRuntime.Package ?? throw new InvalidOperationException(CoreDeviceRuntime.NoMetadataMessage);
                // the task's tunneld (pythonw.exe) holds the runtime's files while it runs
                TryScript(CoreDeviceSetupScripts.StopTask(), "stop task", "Không dừng được tunneld", log);
                RuntimeInstaller.Install(zip, expected, AppPaths.CoreDeviceRoot, log);
            }
            else
            {
                RequireRuntime();
            }

            AddRules(log);
            // decided again here, never taken from the unelevated caller: a task with highest privileges only runs a
            // pythonw.exe that only administrators can replace
            var task = withTask && CoreDeviceRuntime.TaskAllowed;
            if (task)
            {
                RunScript(CoreDeviceSetupScripts.RegisterTask(AppPaths.CoreDevicePythonw, AppPaths.CoreDevicePythonDir, userName),
                          "scheduled task", "Không tạo được tác vụ Scheduled Task", log);
            }
            else
            {
                if (withTask) log("no scheduled task: not an installed copy inside Program Files");
                // an older task may point at a folder that is no longer protected
                TryScript(CoreDeviceSetupScripts.RemoveTask(), "remove task", "Không xóa được tác vụ", log);
            }
            Verify(task, log);
        });

        /// <summary>Only the firewall rules (AddFirewallRules removes the group first, so a second run is the same as one).</summary>
        private static int Rules(string resultFile) => Steps(resultFile, log =>
        {
            RequireElevated();
            RequireRuntime();
            AddRules(log);
            Verify(false, log);
        });

        private static int Remove()
        {
            Action<string> quiet = _ => { };
            // tunneld first: the uninstaller deletes the runtime right after this
            var ok = TryScript(CoreDeviceSetupScripts.StopTask(), "stop task", "Không dừng được tunneld", quiet);
            ok &= TryScript(CoreDeviceSetupScripts.RemoveTask(), "remove task", "Không xóa được tác vụ", quiet);
            ok &= TryScript(CoreDeviceSetupScripts.RemoveFirewallRules(), "remove firewall rules", "Không xóa được luật tường lửa", quiet);
            return ok ? 0 : 1;
        }

        /// <summary>Runs the steps of an elevated mode and writes its outcome and step lines to RESULT.</summary>
        /// <returns>0 when every step succeeded and RESULT was written; 1 otherwise.</returns>
        private static int Steps(string resultFile, Action<Action<string>> steps)
        {
            var lines = new List<string>();
            string outcome;
            try
            {
                steps(lines.Add);
                outcome = SetupHelperResult.Ok;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException
                                      || e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException)
            {
                // InvalidDataException (a zip that is not this build's runtime) is an IOException
                outcome = "failed: " + e.Message;
            }
            var written = WriteResult(resultFile, SetupHelperResult.Format(outcome, lines));
            return written && outcome == SetupHelperResult.Ok ? 0 : 1;
        }

        private static void RequireElevated()
        {
            if (!Elevation.IsElevated) throw new UnauthorizedAccessException("Bước cài đặt không có quyền quản trị.");
        }

        private static void RequireRuntime()
        {
            if (!File.Exists(AppPaths.CoreDevicePython) || !File.Exists(AppPaths.CoreDevicePythonw))
            {
                throw new InvalidOperationException("Chưa có thành phần Điều khiển trực tiếp trong thư mục MacExtend.");
            }
        }

        private static void AddRules(Action<string> log) =>
            RunScript(CoreDeviceSetupScripts.AddFirewallRules(AppPaths.CoreDevicePythonDir), "firewall rules", "Không tạo được luật tường lửa", log);

        /// <summary>What the scripts made must be visible afterwards: all firewall rules, and the task when one was wanted.</summary>
        private static void Verify(bool needsTask, Action<string> log)
        {
            var result = PowerShellRunner.RunAsync(CoreDeviceSetupScripts.Query()).GetAwaiter().GetResult();
            var state = CoreDeviceSetupScripts.ParseQuery(result.Output);
            log("check: rules=" + state.Rules + " task=" + (state.Task ? 1 : 0));
            if (!state.IsComplete(needsTask)) throw new InvalidOperationException("Windows chưa có đủ luật tường lửa hoặc tác vụ sau khi thiết lập.");
        }

        /// <param name="what">English, for the log line.</param>
        /// <param name="failure">Vietnamese, for the outcome the setup window shows.</param>
        private static void RunScript(string script, string what, string failure, Action<string> log)
        {
            var result = PowerShellRunner.RunAsync(script).GetAwaiter().GetResult();
            log(what + ": exit " + result.ExitCode);
            if (!result.Succeeded) throw new InvalidOperationException(failure + " (mã " + result.ExitCode + ").");
        }

        private static bool TryScript(string script, string what, string failure, Action<string> log)
        {
            try
            {
                RunScript(script, what, failure, log);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool WriteResult(string path, string text)
        {
            try
            {
                // a new file only: nothing already at that name (another program's file or link) is ever overwritten
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                {
                    writer.Write(text);
                }
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                return false;
            }
        }
    }
}

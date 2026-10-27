using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core;
using MacExtend.Native;

namespace MacExtend.Services
{
    /// <summary>Runs one uxplay.exe child process with a hidden console and the bundled GStreamer.</summary>
    internal sealed class UxPlayHost : IDisposable
    {
        // Attaching to another console is process-wide state: one stop at a time.
        private static readonly object ConsoleGate = new object();

        private Process _process;

        /// <summary>A line of uxplay output (stdout or stderr). Raised on a thread-pool thread.</summary>
        public event Action<string> Output;

        /// <summary>The process ended on its own (not through <see cref="StopAsync"/>). Raised on a thread-pool thread.</summary>
        public event Action<int> Exited;

        public bool IsRunning
        {
            get
            {
                var p = _process;
                return p != null && !p.HasExited;
            }
        }

        public int ProcessId { get; private set; }

        public void Start(IReadOnlyList<string> args)
        {
            if (IsRunning) throw new InvalidOperationException("uxplay is already running.");

            var psi = new ProcessStartInfo(AppPaths.UxPlayExe, WindowsCommandLine.Join(args))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = AppPaths.DataDir,
            };

            // Only the bundled runtime: a GStreamer or MSYS2 installed elsewhere must not leak in.
            var env = psi.EnvironmentVariables;
            env["PATH"] = AppPaths.UxPlayBin + ";" + Environment.SystemDirectory + ";" +
                          Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            env["GST_PLUGIN_SYSTEM_PATH_1_0"] = AppPaths.PluginDir;
            env["GST_PLUGIN_SCANNER_1_0"] = AppPaths.PluginScanner;
            env["GST_REGISTRY_1_0"] = AppPaths.GstRegistry;
            env.Remove("GST_PLUGIN_PATH");
            env.Remove("GST_PLUGIN_PATH_1_0");
            env.Remove("GST_PLUGIN_SYSTEM_PATH");
            env["GST_DEBUG_NO_COLOR"] = "1";
            // uxplay looks for ~/.uxplayrc under HOME; keep that inside MacExtend's folder.
            env["HOME"] = AppPaths.DataDir;

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) Output?.Invoke(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) Output?.Invoke(e.Data); };
            p.Exited += (_, __) =>
            {
                if (ReferenceEquals(_process, p)) Exited?.Invoke(SafeExitCode(p));
            };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            ProcessId = p.Id;
            _process = p;
        }

        /// <summary>
        /// Sends Ctrl+C so uxplay can unregister from mDNS (the Mac then drops it from the menu
        /// right away), and kills it if it has not exited within <paramref name="grace"/>.
        /// </summary>
        public Task StopAsync(TimeSpan grace)
        {
            var p = Detach();
            return p == null ? Task.CompletedTask : Task.Run(() => Stop(p, grace));
        }

        /// <summary>Same as <see cref="StopAsync"/> but blocks; for Windows shutdown, when the UI thread cannot wait.</summary>
        public void StopNow(TimeSpan grace)
        {
            var p = Detach();
            if (p != null) Stop(p, grace);
        }

        private Process Detach()
        {
            var p = _process;
            _process = null;
            ProcessId = 0;
            return p;
        }

        private static void Stop(Process p, TimeSpan grace)
        {
            try
            {
                if (p.HasExited) return;
                lock (ConsoleGate)
                {
                    var sent = SendCtrlC(p.Id);
                    try
                    {
                        if (!sent || !p.WaitForExit((int)grace.TotalMilliseconds))
                        {
                            TryKill(p);
                            p.WaitForExit(2000);
                        }
                    }
                    finally
                    {
                        // Re-enable Ctrl+C handling only once the event has been delivered;
                        // the ignore flag would otherwise be inherited by the next uxplay.
                        if (sent)
                        {
                            Thread.Sleep(100);
                            Win32.SetConsoleCtrlHandler(null, false);
                        }
                    }
                }
            }
            finally
            {
                p.Dispose();
            }
        }

        public void Dispose()
        {
            var p = _process;
            _process = null;
            if (p == null) return;
            TryKill(p);
            p.Dispose();
        }

        private static bool SendCtrlC(int pid)
        {
            Win32.FreeConsole();
            if (!Win32.AttachConsole((uint)pid)) return false;
            Win32.SetConsoleCtrlHandler(null, true);
            var ok = Win32.GenerateConsoleCtrlEvent(Win32.CTRL_C_EVENT, 0);
            Win32.FreeConsole();
            if (!ok) Win32.SetConsoleCtrlHandler(null, false);
            return ok;
        }

        private static void TryKill(Process p)
        {
            try
            {
                if (!p.HasExited) p.Kill();
            }
            catch (InvalidOperationException)
            {
                // already exited
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // exiting; nothing more to do
            }
        }

        private static int SafeExitCode(Process p)
        {
            try
            {
                return p.ExitCode;
            }
            catch (InvalidOperationException)
            {
                return -1;
            }
        }
    }
}

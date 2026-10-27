using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MacExtend.Services
{
    internal sealed class ScriptResult
    {
        public ScriptResult(int exitCode, string output, bool cancelled = false)
        {
            ExitCode = exitCode;
            Output = output;
            Cancelled = cancelled;
        }

        public int ExitCode { get; }
        public string Output { get; }
        /// <summary>The user declined the administrator prompt.</summary>
        public bool Cancelled { get; }
        public bool Succeeded => ExitCode == 0 && !Cancelled;
    }

    /// <summary>Runs Windows PowerShell scripts for firewall and network settings, which have no simple managed API.</summary>
    internal static class PowerShellRunner
    {
        private const int ErrorCancelled = 1223;

        public static string Literal(string value) => "'" + value.Replace("'", "''") + "'";

        /// <summary>Runs as the current user and captures standard output.</summary>
        public static Task<ScriptResult> RunAsync(string script) => Task.Run(() =>
        {
            var psi = Start("[Console]::OutputEncoding = [Text.Encoding]::UTF8\n" + script);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            try
            {
                using (var p = Process.Start(psi))
                {
                    var stderr = p.StandardError.ReadToEndAsync();
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    stderr.Wait();
                    return new ScriptResult(p.ExitCode, output);
                }
            }
            catch (Win32Exception e)
            {
                return new ScriptResult(-1, e.Message);
            }
        });

        /// <summary>Runs with administrator rights after the Windows UAC prompt. Only the exit code comes back.</summary>
        public static Task<ScriptResult> RunElevatedAsync(string script) => Task.Run(() =>
        {
            var psi = Start("$ErrorActionPreference = 'Stop'\ntry {\n" + script + "\n  exit 0\n} catch {\n  exit 1\n}");
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            try
            {
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return new ScriptResult(p.ExitCode, "");
                }
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
            {
                return new ScriptResult(-1, "", cancelled: true);
            }
            catch (Win32Exception e)
            {
                return new ScriptResult(-1, e.Message);
            }
        });

        private static ProcessStartInfo Start(string script)
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var exe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            return new ProcessStartInfo(exe, "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded);
        }
    }
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>
    /// Gets a tunneld tunnel to one device for a Wi-Fi session. An already running tunneld is reused and never stopped;
    /// otherwise it is started (the Scheduled Task, or pythonw.exe with UAC for a copy without the task) and shut down
    /// by <see cref="StopAsync"/>. Every network call has a timeout and none runs on the caller's thread.
    /// </summary>
    internal sealed class TunnelController
    {
        public static readonly TimeSpan TunnelTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);
        private const int ErrorCancelled = 1223;

        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(2),
        };
        private static readonly Uri Root = new Uri("http://" + TunneldListing.Host + ":" + TunneldListing.Port + "/");
        private static readonly Uri ShutdownUri = new Uri(Root, "shutdown");

        // the four firewall rules a copy without the task adds itself: once per run, again after a failed start
        private static bool _rulesAdded;

        private bool _ours;
        private bool _startedWithTask;

        public async Task<TunnelOutcome> EnsureAsync(string udid, CancellationToken token)
        {
            var listing = await ListAsync(token).ConfigureAwait(false);
            var step = TunnelPlan.First(listing != null, listing != null && TunneldListing.HasTunnel(listing, udid));
            Log("tunneld at start: " + TunneldListing.Summary(listing) + "; step " + step);
            if (step == TunnelStep.UseExisting) return TunnelOutcome.Ready;

            var rulesThisCall = false;
            if (step == TunnelStep.StartTunneld)
            {
                if (!WindowsSetupState.UseTask && !_rulesAdded)
                {
                    var rules = await AddRulesAsync().ConfigureAwait(false);
                    if (rules == TunnelOutcome.AdminDeclined) return rules;
                    rulesThisCall = true;
                }
                var started = await Task.Run(() => StartTunneld()).ConfigureAwait(false);
                if (started != TunnelOutcome.Ready) return await FailedAsync(started, rulesThisCall).ConfigureAwait(false);
                _ours = TunnelPlan.ShutdownAfter(step);
                _startedWithTask = WindowsSetupState.UseTask;
            }

            var last = "";
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TunnelTimeout)
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
                var json = await ListAsync(token).ConfigureAwait(false);
                var summary = TunneldListing.Summary(json);
                if (summary != last)
                {
                    last = summary;
                    Log("tunneld: " + summary);
                }
                if (json != null && TunneldListing.HasTunnel(json, udid)) return TunnelOutcome.Ready;
            }
            return await FailedAsync(TunnelOutcome.Timeout, rulesThisCall).ConfigureAwait(false);
        }

        /// <summary>Shuts down the tunneld this controller started; a tunneld it found running is left alone.</summary>
        public async Task StopAsync()
        {
            if (!_ours) return;
            _ours = false;
            try
            {
                using (await Http.GetAsync(ShutdownUri).ConfigureAwait(false)) { }
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
            {
                // tunneld exits while answering; no answer is the usual outcome
            }

            var watch = Stopwatch.StartNew();
            while (await Task.Run(() => IsListening()).ConfigureAwait(false))
            {
                if (watch.Elapsed >= StopTimeout)
                {
                    if (_startedWithTask) await Task.Run(() => EndTask()).ConfigureAwait(false);
                    else Log("tunneld still answers after shutdown; it was started with UAC and cannot be stopped from here");
                    return;
                }
                await Task.Delay(500).ConfigureAwait(false);
            }
        }

        private static async Task<TunnelOutcome> FailedAsync(TunnelOutcome outcome, bool rulesThisCall)
        {
            Log("Wi-Fi tunnel failed: " + outcome);
            // a copy without the task may have lost its rules: add them (idempotent) so the next try can work
            if (!WindowsSetupState.UseTask && !rulesThisCall) await AddRulesAsync().ConfigureAwait(false);
            return outcome;
        }

        private static async Task<TunnelOutcome> AddRulesAsync()
        {
            var result = await CoreDeviceRuntime.AddFirewallRulesAsync().ConfigureAwait(false);
            if (result == "ok") _rulesAdded = true;
            return result == "cancelled" ? TunnelOutcome.AdminDeclined : TunnelOutcome.Ready;
        }

        private static async Task<string> ListAsync(CancellationToken token)
        {
            try
            {
                using (var response = await Http.GetAsync(Root, token).ConfigureAwait(false))
                {
                    return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
                }
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !token.IsCancellationRequested))
            {
                return null;
            }
        }

        private static bool IsListening()
        {
            try
            {
                using (var client = new TcpClient())
                {
                    return client.ConnectAsync(TunneldListing.Host, TunneldListing.Port).Wait(500) && client.Connected;
                }
            }
            catch (AggregateException)
            {
                return false;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static TunnelOutcome StartTunneld()
        {
            if (!CoreDeviceRuntime.IsInstalled) return TunnelOutcome.SetupMissing;
            try
            {
                if (WindowsSetupState.UseTask) return RunSchtasks("/Run") ? TunnelOutcome.Ready : TunnelOutcome.SetupMissing;
                // the task's own command line, started with UAC: the runtime's pythonw.exe and the fixed bootstrap
                Process.Start(new ProcessStartInfo(AppPaths.CoreDevicePythonw, WindowsCommandLine.Join(CoreDeviceArguments.TunneldBootstrap()))
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppPaths.CoreDevicePythonDir,
                    WindowStyle = ProcessWindowStyle.Hidden,
                })?.Dispose();
                return TunnelOutcome.Ready;
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
            {
                return TunnelOutcome.AdminDeclined;
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
            {
                return TunnelOutcome.SetupMissing;
            }
        }

        private static void EndTask()
        {
            Log("tunneld still answers after shutdown; ending the task: " + (RunSchtasks("/End") ? "ok" : "failed"));
        }

        private static bool RunSchtasks(string verb)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", verb + " /TN " + WindowsCommandLine.Quote(CoreDeviceSetupScripts.TaskFullName))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }))
                {
                    if (p == null || !p.WaitForExit(10000)) return false;
                    return p.ExitCode == 0;
                }
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
            {
                return false;
            }
        }

        private static void Log(string line)
        {
            try
            {
                using (var log = new RotatingLog(AppPaths.CoreDeviceLog, 512 * 1024)) log.WriteLine("[tunnel] " + line);
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                // a log that cannot be written never fails a session
            }
        }
    }
}

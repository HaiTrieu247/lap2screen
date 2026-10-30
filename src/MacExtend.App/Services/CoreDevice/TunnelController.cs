using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.Native;

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
            MaxResponseContentBufferSize = 1024 * 1024,
        };
        private static readonly Uri Root = new Uri("http://" + TunneldListing.Host + ":" + TunneldListing.Port + "/");
        private static readonly Uri ShutdownUri = new Uri(Root, "shutdown");

        // the four firewall rules a copy without the task adds itself: once per run, again after a failed start
        private static bool _rulesAdded;

        private static readonly TimeSpan StartJoinTimeout = TimeSpan.FromSeconds(15);

        private volatile bool _stopRequested;
        private bool _ours;
        private bool _startedWithTask;
        private Task _startPhase = Task.CompletedTask;

        public async Task<TunnelOutcome> EnsureAsync(string udid, CancellationToken token)
        {
            // Wi-Fi needs an account that elevates by itself; a standard account is told at once, with no UAC
            if (!Elevation.IsAdminAccount) return TunnelOutcome.NeedsAdminAccount;
            _stopRequested = false;

            var listing = await ListAsync(token).ConfigureAwait(false);
            var answers = TunneldListing.IsListing(listing);
            var portListens = !answers && await Task.Run(() => IsListening()).ConfigureAwait(false);
            var step = TunnelPlan.First(answers, answers && TunneldListing.HasTunnel(listing, udid), portListens);
            Log("tunneld at start: " + TunneldListing.Summary(listing) + "; step " + step);
            if (step == TunnelStep.UseExisting) return TunnelOutcome.Ready;
            if (step == TunnelStep.PortBusy) return TunnelOutcome.PortInUse;

            var rulesThisCall = false;
            var startedByUs = false;
            if (step == TunnelStep.StartTunneld)
            {
                var phase = StartAsync(token);
                _startPhase = phase;
                var started = await phase.ConfigureAwait(false);
                if (started.Outcome != TunnelOutcome.Ready) return await FailedAsync(started.Outcome, started.RulesAdded, startedByUs: false).ConfigureAwait(false);
                rulesThisCall = started.RulesAdded;
                startedByUs = true;
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
            return await FailedAsync(TunnelOutcome.Timeout, rulesThisCall, startedByUs).ConfigureAwait(false);
        }

        private struct StartResult
        {
            public TunnelOutcome Outcome;
            public bool RulesAdded;
        }

        /// <summary>The rules (a copy without the task) and the start of tunneld; ownership is claimed only once it started with the port closed.</summary>
        private async Task<StartResult> StartAsync(CancellationToken token)
        {
            var result = new StartResult();
            if (!WindowsSetupState.UseTask && !_rulesAdded)
            {
                result.Outcome = await AddRulesAsync().ConfigureAwait(false);
                result.RulesAdded = true;
                if (result.Outcome == TunnelOutcome.AdminDeclined) return result;
            }
            // the UAC prompt cannot be cancelled: do not go on to start tunneld for a session that was cancelled meanwhile
            token.ThrowIfCancellationRequested();
            result.Outcome = await Task.Run(() => StartTunneld()).ConfigureAwait(false);
            if (result.Outcome == TunnelOutcome.Ready)
            {
                if (_stopRequested)
                {
                    // StopAsync gave up waiting for this start: what it started is shut down here and never claimed
                    await ShutDownAsync(WindowsSetupState.UseTask).ConfigureAwait(false);
                    throw new OperationCanceledException();
                }
                _ours = true;
                _startedWithTask = WindowsSetupState.UseTask;
            }
            return result;
        }

        /// <summary>Shuts down the tunneld this controller started; a tunneld it found running is left alone.</summary>
        public async Task StopAsync()
        {
            // a start still in flight (UAC prompt, schtasks) decides whether there is anything to stop
            var phase = _startPhase;
            _stopRequested = true;
            if (!phase.IsCompleted) await Task.WhenAny(phase, Task.Delay(StartJoinTimeout)).ConfigureAwait(false);
            if (!_ours) return;
            _ours = false;
            await ShutDownAsync(_startedWithTask).ConfigureAwait(false);
        }

        /// <summary>GET /shutdown, then, if the port still listens after 10 s, end the task (task mode) or only log (UAC mode).</summary>
        private static async Task ShutDownAsync(bool withTask)
        {
            try
            {
                using (await Http.GetAsync(ShutdownUri).ConfigureAwait(false)) { }
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is InvalidOperationException)
            {
                // tunneld exits while answering; no answer is the usual outcome
            }

            var watch = Stopwatch.StartNew();
            while (await Task.Run(() => IsListening()).ConfigureAwait(false))
            {
                if (watch.Elapsed >= StopTimeout)
                {
                    if (withTask) await Task.Run(() => EndTask()).ConfigureAwait(false);
                    else Log("tunneld still answers after shutdown; it was started with UAC and cannot be stopped from here");
                    return;
                }
                await Task.Delay(500).ConfigureAwait(false);
            }
        }

        private static async Task<TunnelOutcome> FailedAsync(TunnelOutcome outcome, bool rulesThisCall, bool startedByUs)
        {
            Log("Wi-Fi tunnel failed: " + outcome);
            // a copy without the task may have lost its rules: add them (idempotent) so the next try can work
            if (!WindowsSetupState.UseTask && !rulesThisCall && TunnelPlan.ReaddRulesAfter(outcome, startedByUs)) await AddRulesAsync().ConfigureAwait(false);
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
            // InvalidOperationException: a charset .NET Framework cannot decode, so not a tunneld
            catch (Exception e) when (e is HttpRequestException || e is InvalidOperationException || (e is TaskCanceledException && !token.IsCancellationRequested))
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
                    var connect = client.ConnectAsync(TunneldListing.Host, TunneldListing.Port);
                    // an abandoned attempt faults when the client is disposed: observe it so it never reaches the crash log
                    connect.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    return connect.Wait(500) && client.Connected;
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
                using (var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), verb + " /TN " + WindowsCommandLine.Quote(CoreDeviceSetupScripts.TaskFullName))
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
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // a log that cannot be written never fails a session
            }
        }
    }
}

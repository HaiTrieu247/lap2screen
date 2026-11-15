using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>
    /// Runs "Điều khiển trực tiếp" sessions, one at a time, end to end: the Wi-Fi tunnel (TunnelController) or the
    /// cable's userspace tunnel, the Developer Disk Image mount, serve-web in a job of its own until it serves its viewer,
    /// the cable leaving and coming back, and the stop: touch and keys released (2 s), exactly one Ctrl+C, up to 40 s for
    /// serve-web's cleanup, then its job is killed; a tunneld this session started is shut down after serve-web is gone.
    ///
    /// Threads. Every input goes into the session's thread-safe <see cref="SessionMachine"/> on the thread it happens on
    /// (a pool thread, serve-web's output reader, the caller of Start or StopAsync); the action it returns runs on the
    /// thread pool. Nothing here waits for the UI thread, so a stop also completes while the app exits. The UI reads only
    /// <see cref="View"/>: an immutable snapshot taken on the UI thread right before <see cref="StateChanged"/>, which is
    /// always raised there.
    ///
    /// Generations. A session holds the <see cref="SessionSlot"/> from its start until its teardown is over; every
    /// callback carries its session and is dropped unless that session still holds the slot, so a late answer of an old
    /// session never touches a new one. A Start while the previous session is still stopping waits for that teardown
    /// ("Đang dừng phiên trước...").
    /// </summary>
    internal sealed class CoreDeviceService : IDisposable
    {
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan Grace = TimeSpan.FromSeconds(40);
        private static readonly TimeSpan ReleaseDeadline = TimeSpan.FromSeconds(2);
        // the readers take in an exited serve-web's last lines, so its error can be explained
        private static readonly TimeSpan OutputSettle = TimeSpan.FromSeconds(2);
        // a cancelled readiness wait returns within one viewer request (3 s)
        private static readonly TimeSpan ReadinessJoin = TimeSpan.FromSeconds(10);
        // TunnelController.StopAsync bounds itself (about 60 s with a start in flight); this only guards against a hang
        private static readonly TimeSpan TunnelStopBound = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan ExitPoll = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan PresencePoll = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ReturnPoll = TimeSpan.FromSeconds(2);

        private readonly Dispatcher _ui;
        private readonly TunnelController _tunnel = new TunnelController();
        private readonly object _gate = new object();
        // _slot and _session are guarded by _gate; _disposed is written under it
        private readonly SessionSlot _slot = new SessionSlot();
        private Session _session;
        private volatile bool _disposed;
        private volatile SessionView _view = SessionView.Idle;
        // UI thread only: the snapshot StateChanged last announced
        private SessionView _raised = SessionView.Idle;

        public CoreDeviceService(Dispatcher ui)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        /// <summary>Always raised on the UI thread, once per new <see cref="View"/>.</summary>
        public event Action StateChanged;

        /// <summary>The session as the UI shows it. Replaced on the UI thread only; every property below reads it.</summary>
        public SessionView View => _view;

        public SessionPhase Phase => _view.Phase;
        public SessionFailure Failure => _view.Failure;
        public CoreDeviceRoute Route => _view.Route;
        public string Udid => _view.Udid;
        /// <summary>serve-web's loopback port while its viewer can be used; 0 otherwise.</summary>
        public int Port => _view.Port;
        public string ViewerUrl => _view.ViewerUrl;
        /// <summary>A session runs, stops, or waits for the previous one to stop.</summary>
        public bool IsActive => _view.IsActive;
        /// <summary>The last stop had to kill serve-web: its cleanup (camera and microphone release) did not finish.</summary>
        public bool LastStopWasForced => _view.LastStopWasForced;
        public bool WaitingForPreviousStop => _view.WaitingForPreviousStop;
        public string StatusText => _view.StatusText;

        /// <summary>
        /// Opens a session to <paramref name="udid"/> over <paramref name="route"/>. Any thread. Ignored while a session
        /// runs; while the previous session is still stopping it waits for that stop (the latest such request wins).
        /// </summary>
        /// <exception cref="ArgumentException">Not a device UDID, or no route.</exception>
        public void Start(string udid, CoreDeviceRoute route)
        {
            var request = new SessionRequest(udid, route);
            Session started = null;
            var first = SessionAction.None;
            int waitingFor;
            lock (_gate)
            {
                if (_disposed) return;
                waitingFor = _session?.Generation ?? 0;
                switch (_slot.Open(request, _session != null && _session.Machine.IsOver))
                {
                    case OpenOutcome.AlreadyRunning:
                        return;
                    case OpenOutcome.Started:
                        started = Begin(request, out first);
                        break;
                }
            }
            if (started != null)
            {
                LogStart(started);
                Do(started, first);
            }
            else
            {
                Write("[coredevice] an open waits for session #" + waitingFor + " to stop");
            }
            Publish();
        }

        /// <summary>
        /// Stops the session and drops an open waiting for it. Any thread; nothing in the stop needs the UI thread.
        /// Completes when serve-web and a tunneld this session started are gone, at once when nothing runs.
        /// </summary>
        public Task StopAsync()
        {
            Session s;
            lock (_gate)
            {
                _slot.CancelQueued();
                s = _session != null && _slot.IsCurrent(_session.Generation) ? _session : null;
            }
            if (s != null) RequestStop(s);
            Publish();
            return s?.Stopped.Task ?? Task.CompletedTask;
        }

        /// <summary>
        /// Asks the session to stop and returns at once (N5): app exit awaits <see cref="StopAsync"/>, bounded, before
        /// this. A stop still running when the process ends is cut short by the job objects, which kill serve-web with
        /// their last handle. No StateChanged after this.
        /// </summary>
        public void Dispose()
        {
            Session s;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _slot.CancelQueued();
                s = _session != null && _slot.IsCurrent(_session.Generation) ? _session : null;
            }
            if (s != null) RequestStop(s);
        }

        // ---- driving the machine -------------------------------------------------------

        /// <summary>Under _gate: the session that now holds the slot, started.</summary>
        private Session Begin(SessionRequest request, out SessionAction first)
        {
            var s = new Session(_slot.Generation, request);
            first = s.Machine.Start();
            _session = s;
            return s;
        }

        private void RequestStop(Session s)
        {
            var phase = s.Machine.Phase;
            if (s.Machine.IsActive && !s.Machine.StopRequested) Log(s, "stop requested in " + phase);
            Do(s, s.Machine.Stop());
        }

        /// <summary>Feeds one input to the session's machine and does what it answers; dropped for a session that no longer holds the slot.</summary>
        private void Step(Session s, Func<SessionMachine, SessionAction> input)
        {
            if (!IsCurrent(s)) return;
            Do(s, input(s.Machine));
            Publish();
        }

        private bool IsCurrent(Session s)
        {
            lock (_gate) return _slot.IsCurrent(s.Generation);
        }

        /// <summary>Starts the machine's action on the thread pool. A session that is over (stopping or failed) is torn down.</summary>
        private void Do(Session s, SessionAction action)
        {
            switch (action)
            {
                case SessionAction.StartTunnel:
                    _ = Spawn(s, "the Wi-Fi tunnel", () => TunnelAsync(s));
                    break;
                case SessionAction.MountImage:
                    _ = Spawn(s, "the image mount", () => MountAsync(s));
                    break;
                case SessionAction.StartServeWeb:
                    _ = Spawn(s, "the serve-web start", () => StartServeWebAsync(s));
                    break;
                case SessionAction.StopServeWeb:
                    // over (a stop, a failure): the teardown stops serve-web; otherwise the cable left and it restarts later
                    if (!s.Machine.IsOver) _ = Spawn(s, "the serve-web stop", () => StopServeWebAsync(s));
                    break;
                case SessionAction.WatchUsb:
                    _ = Spawn(s, "the cable watch", () => WatchUsbReturnAsync(s));
                    break;
            }
            if (s.Machine.IsOver) BeginTeardown(s);
        }

        /// <summary>
        /// Session work on the thread pool. Each piece handles the failures it expects; anything else (a bug, a system
        /// failure) must neither leave the session hanging nor reach the crash log as an unobserved task: it is logged by
        /// type and fails the session.
        /// </summary>
        private Task Spawn(Session s, string what, Func<Task> work) => Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log(s, "unexpected " + e.GetType().Name + " in " + what);
                Step(s, m => m.Fault());
            }
        });

        // ---- the machine's actions ------------------------------------------------------

        private async Task TunnelAsync(Session s)
        {
            TunnelOutcome outcome;
            try
            {
                outcome = await _tunnel.EnsureAsync(s.Udid, s.Cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // stopped meanwhile: the teardown shuts down whatever this start began
                return;
            }
            Log(s, "Wi-Fi tunnel: " + outcome);
            Step(s, m => m.TunnelReady(outcome));
        }

        private async Task MountAsync(Session s)
        {
            CommandResult result;
            try
            {
                // over the cable lockdown says at once whether the image is mounted (it stays until the device restarts),
                // which spares auto-mount's tunnel; anything but a clear yes mounts as before
                if (s.Route == CoreDeviceRoute.Usb)
                {
                    var listed = await CoreDeviceCommands.MountedImagesAsync(s.Udid).ConfigureAwait(false);
                    if (s.Cts.IsCancellationRequested) return;
                    if (listed.Succeeded && DeviceQueries.ParseDeveloperImageMounted(listed.Stdout) == true)
                    {
                        Log(s, "image mount: already mounted");
                        Step(s, m => m.ImageMounted(true, CliError.None));
                        return;
                    }
                }
                result = await CoreDeviceCommands.MountAsync(s.Udid, s.Route).ConfigureAwait(false);
            }
            catch (CoreDeviceHostException e)
            {
                // the runtime's python.exe could not be started in a job: a failed command
                Log(s, "image mount could not run: " + e.Message);
                Step(s, m => m.ImageMounted(false, CliError.Other));
                return;
            }
            // the command cannot be cancelled; a stop meanwhile only makes its answer moot
            if (s.Cts.IsCancellationRequested) return;
            Log(s, "image mount: " + (result.Succeeded ? "ok" : "failed, " + result.Error + (result.TimedOut ? " (timed out)" : "")));
            Step(s, m => m.ImageMounted(result.Succeeded, result.Error));
        }

        private Task StartServeWebAsync(Session s)
        {
            ServeWebRun run;
            string problem = null;
            lock (s.RunGate)
            {
                // a stop or a lost cable since the machine asked: start nothing. Under RunGate, a stop finds either no
                // serve-web or this one, started.
                if (s.Machine.Phase != SessionPhase.Starting) return Task.CompletedTask;
                run = Launch(s, ref problem);
                if (run != null)
                {
                    s.Run = run;
                    run.Readiness = Spawn(s, "the serve-web readiness wait", () => AwaitReadyAsync(s, run));
                }
            }
            if (run == null)
            {
                Log(s, "serve-web did not start: " + problem);
                Step(s, m => m.ServeWebFailed(ServeWebLogKind.Error, false));
            }
            else
            {
                Log(s, "serve-web started on port " + run.Port);
            }
            return Task.CompletedTask;
        }

        /// <summary>serve-web in a kill-on-close job, on a free loopback port; null (and why) when it could not start.</summary>
        private ServeWebRun Launch(Session s, ref string problem)
        {
            CoreDeviceProcessHost host = null;
            try
            {
                var options = new CoreDeviceOptions(AppPaths.CoreDevicePython, s.Udid,
                                                    s.Route == CoreDeviceRoute.Wifi ? CoreDeviceTransport.Tunnel : CoreDeviceTransport.Userspace,
                                                    FreeLoopbackPort());
                host = new CoreDeviceProcessHost();
                var run = new ServeWebRun(host, options.HttpPort);
                // before Start, so the readers report from the first line; no RawLineHandler: lines are classified by the host
                host.StatusLine += line => OnStatusLine(s, run, line);
                // the app's direct_input.py, not serve-web: no viewer, and the media stream that blocks the device's
                // camera and microphone is open only while input comes
                if (!File.Exists(AppPaths.DirectInputScript)) throw new ArgumentException("the input helper " + AppPaths.DirectInputScript + " is missing");
                host.Start(options.PythonPath, CoreDeviceArguments.DirectInput(options, AppPaths.DirectInputScript),
                           CoreDeviceArguments.Environment(Environment.GetEnvironmentVariables(), options), false, null);
                return run;
            }
            catch (Exception e) when (e is CoreDeviceHostException || e is SocketException || e is ArgumentException)
            {
                host?.Dispose();
                problem = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        private async Task AwaitReadyAsync(Session s, ServeWebRun run)
        {
            ReadyFailure failure;
            try
            {
                failure = await run.Host.WaitReadyAsync(run.Port, StartupTimeout, run.Cts.Token, line => Log(s, "serve-web " + line)).ConfigureAwait(false);
            }
            catch (CoreDeviceHostException e)
            {
                // the job or the TCP table could not be read
                Log(s, "serve-web readiness check failed: " + e.Message);
                Step(s, m => m.ServeWebFailed(ServeWebLogKind.Error, false));
                return;
            }
            // being stopped: the stop owns this serve-web now
            if (failure == ReadyFailure.Cancelled) return;
            if (failure == ReadyFailure.None)
            {
                run.MarkReady();
                Volatile.Write(ref s.ServingPort, run.Port);
                Log(s, "serve-web ready");
                Step(s, m => m.ServeWebReady());
                _ = Spawn(s, "the serve-web exit watch", () => WatchExitAsync(s, run));
                if (s.Route == CoreDeviceRoute.Usb) _ = Spawn(s, "the cable presence watch", () => WatchUsbPresenceAsync(s, run));
                return;
            }
            if (failure == ReadyFailure.ExitedEarly) run.Host.WaitForOutput(OutputSettle);
            var lastFatal = run.Host.LastFatal;
            Log(s, "serve-web not ready: " + failure + (lastFatal != ServeWebLogKind.Other ? ", " + lastFatal : ""));
            Step(s, m => m.ServeWebFailed(lastFatal, failure == ReadyFailure.TimedOut));
        }

        /// <summary>The cable left (WaitingForDevice): stop this serve-web; the machine restarts once the device is back.</summary>
        private async Task StopServeWebAsync(Session s)
        {
            var run = s.CurrentRun();
            if (run != null)
            {
                await StopRunAsync(s, run).ConfigureAwait(false);
                return;
            }
            // none runs (it exited already, or never started): that counts as exited
            Step(s, m => m.ServeWebExited(ServeWebLogKind.Other));
        }

        /// <summary>Stops one serve-web exactly once, however many callers ask (a lost cable, its own exit, the teardown).</summary>
        private Task StopRunAsync(Session s, ServeWebRun run) => run.StopOnce(() => StopRunCoreAsync(s, run));

        /// <summary>
        /// The probe's stop of one serve-web: touch and keys released within 2 s, exactly one Ctrl+C, up to 40 s for its
        /// cleanup, then the job is killed (CoreDeviceProcessHost.StopAsync); then the machine hears that it exited.
        /// Runs on the thread pool and never waits for the UI thread.
        /// </summary>
        private async Task StopRunCoreAsync(Session s, ServeWebRun run)
        {
            var lastFatal = ServeWebLogKind.Other;
            try
            {
                run.Cts.Cancel();
                // the readiness wait reads the job: let it return before the job handle is closed
                await Task.WhenAny(run.Readiness, Task.Delay(ReadinessJoin)).ConfigureAwait(false);
                Interlocked.CompareExchange(ref s.ServingPort, 0, run.Port);
                if (run.IsReady && run.IsEmpty() == false)
                {
                    using (var client = new CoreDeviceClient(run.Port))
                    {
                        var released = await client.ReleaseAllAsync(ReleaseDeadline, force: true).ConfigureAwait(false);
                        Log(s, "touch and keys: " + released);
                    }
                }
                StopExit exit;
                try
                {
                    exit = await run.Host.StopAsync(AppPaths.SelfExe, Grace, line => Log(s, "serve-web " + line)).ConfigureAwait(false);
                }
                catch (CoreDeviceHostException e)
                {
                    // the job could not be read: end it
                    Log(s, "serve-web stop failed (" + e.Message + "); killing it");
                    run.Host.Kill();
                    exit = StopExit.Killed;
                }
                s.LastStopKilled = exit == StopExit.Killed;
                run.Host.WaitForOutput(OutputSettle);
                lastFatal = run.Host.LastFatal;
                Log(s, "serve-web stopped: " + exit + Cleanup(run.Host, exit) + (lastFatal != ServeWebLogKind.Other ? ", last error " + lastFatal : ""));
            }
            finally
            {
                s.ClearRun(run);
                run.Dispose();
            }
            Step(s, m => m.ServeWebExited(lastFatal));
        }

        private static string Cleanup(CoreDeviceProcessHost host, StopExit exit)
        {
            if (exit == StopExit.AlreadyExited) return "";
            if (host.CleanupReportedComplete) return ", cleanup complete";
            var step = host.FailedCleanupStep;
            return step != null ? ", cleanup step failed: " + step : ", cleanup not reported";
        }

        // ---- teardown --------------------------------------------------------------------

        private void BeginTeardown(Session s)
        {
            if (Interlocked.Exchange(ref s.TearingDown, 1) == 0) _ = Spawn(s, "the teardown", () => TeardownAsync(s));
        }

        /// <summary>
        /// The end of a session, once: cancel what is starting, stop serve-web, then shut down a tunneld this session
        /// started (serve-web's cleanup reaches the device through it). Then the slot is free and a waiting open starts.
        /// </summary>
        private async Task TeardownAsync(Session s)
        {
            try
            {
                // before TunnelController.StopAsync: EnsureAsync must find its token cancelled first
                s.Cts.Cancel();
                var run = s.CurrentRun();
                if (run != null) await StopRunAsync(s, run).ConfigureAwait(false);
                if (s.Route == CoreDeviceRoute.Wifi) await StopTunnelAsync(s).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // the slot is freed whatever happened; serve-web's job dies with its handle at the latest
                Log(s, "unexpected " + e.GetType().Name + " while stopping");
            }
            Finish(s);
        }

        private async Task StopTunnelAsync(Session s)
        {
            var stop = _tunnel.StopAsync();
            if (await Task.WhenAny(stop, Task.Delay(TunnelStopBound)).ConfigureAwait(false) == stop)
            {
                await stop.ConfigureAwait(false);
                return;
            }
            Log(s, "the Wi-Fi tunnel is still stopping after " + TunnelStopBound.TotalSeconds + " s");
            _ = stop.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void Finish(Session s)
        {
            s.Machine.StopFinished();
            var phase = s.Machine.Phase;
            Log(s, "session over: " + (phase == SessionPhase.Failed ? "failed, " + s.Machine.Failure : phase.ToString()));
            Session next = null;
            var first = SessionAction.None;
            lock (_gate)
            {
                // Dispose drops a waiting open first, so a request handed back here is still wanted
                var request = _slot.TornDown(s.Generation);
                if (request != null) next = Begin(request, out first);
            }
            s.Stopped.TrySetResult(true);
            if (next != null)
            {
                LogStart(next);
                Do(next, first);
            }
            Publish();
        }

        // ---- watchers ----------------------------------------------------------------------

        /// <summary>serve-web's job emptied without a stop: it exited on its own (an error, the camera in use, the device gone).</summary>
        private async Task WatchExitAsync(Session s, ServeWebRun run)
        {
            var token = run.Cts.Token;
            while (true)
            {
                try
                {
                    await Task.Delay(ExitPoll, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // being stopped: the stop owns this serve-web
                    return;
                }
                var empty = run.IsEmpty();
                if (empty == null) return;
                if (!empty.Value) continue;
                // emptied by a stop that began meanwhile: that stop reports it
                if (token.IsCancellationRequested) return;
                Log(s, "serve-web exited on its own");
                await StopRunAsync(s, run).ConfigureAwait(false);
                return;
            }
        }

        /// <summary>
        /// serve-web over the userspace tunnel never recovers from an unplug: notice the device leaving usbmux. Asks the
        /// Apple Mobile Device Service itself (no Python per poll); an answer it could not give is unknown, not a miss.
        /// </summary>
        private async Task WatchUsbPresenceAsync(Session s, ServeWebRun run)
        {
            var token = run.Cts.Token;
            var presence = new UsbPresence(s.Udid);
            var answered = true;
            while (true)
            {
                try
                {
                    await Task.Delay(PresencePoll, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                var listed = await CoreDeviceCommands.UsbmuxUsbDevicesAsync().ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                // only when that changes, so a service that stays away does not fill the log
                if ((listed != null) != answered)
                {
                    answered = listed != null;
                    Log(s, answered ? "cable presence check: the Apple service answers again" : "cable presence check: no answer from the Apple service, unknown");
                }
                if (!presence.Gone(listed)) continue;
                Log(s, "device left the cable");
                Step(s, m => m.UsbDeviceGone());
                return;
            }
        }

        private async Task WatchUsbReturnAsync(Session s)
        {
            // one wait can be handed WatchUsb twice (a lost cable and serve-web's own exit racing): one watcher is enough
            if (Interlocked.Exchange(ref s.WatchingCable, 1) == 1) return;
            try
            {
                var token = s.Cts.Token;
                while (s.Machine.Phase == SessionPhase.WaitingForDevice)
                {
                    try
                    {
                        await Task.Delay(ReturnPoll, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    if (!UsbPresence.IsListed(await UsbDevicesAsync().ConfigureAwait(false), s.Udid)) continue;
                    Log(s, "device back on the cable");
                    Step(s, m => m.UsbDeviceBack());
                    return;
                }
            }
            finally
            {
                Volatile.Write(ref s.WatchingCable, 0);
            }
        }

        /// <summary>`usbmux list --usb`; nothing listed when the runtime's python.exe cannot be started.</summary>
        private static async Task<IReadOnlyList<string>> UsbDevicesAsync()
        {
            try
            {
                return await CoreDeviceCommands.UsbDevicesAsync().ConfigureAwait(false);
            }
            catch (CoreDeviceHostException)
            {
                return new string[0];
            }
        }

        /// <summary>serve-web's output reader thread. It must never throw: that would end the reader and, unhandled, the app.</summary>
        private void OnStatusLine(Session s, ServeWebRun run, ServeWebLogLine line)
        {
            try
            {
                // classified kinds only (the host drops Other); Detail is from a fixed vocabulary
                if (line.Kind == ServeWebLogKind.Listening) Log(s, "serve-web listening on port " + line.Port);
                else Log(s, "serve-web: " + line.Kind + (line.Detail != null ? " " + line.Detail : ""));
                // over Wi-Fi serve-web waits for tunneld and comes back by itself; a serve-web that is no longer the session's changes nothing
                if (s.Route == CoreDeviceRoute.Wifi && (line.Kind == ServeWebLogKind.DeviceDisconnected || line.Kind == ServeWebLogKind.DeviceReconnected)
                    && s.CurrentRun() == run)
                {
                    Step(s, m => m.Log(line.Kind));
                }
            }
            catch (Exception)
            {
                // nothing a status line could do is worth serve-web's output pipe filling up
            }
        }

        // ---- the UI's snapshot --------------------------------------------------------------

        /// <summary>
        /// Tells the UI. On the UI thread the snapshot is taken at once, so a caller of Start or StopAsync reads the new
        /// state right away; StateChanged itself always comes through the dispatcher, never inside a caller.
        /// </summary>
        private void Publish()
        {
            if (_ui.CheckAccess()) _view = Capture();
            _ = _ui.BeginInvoke(new Action(Raise));
        }

        /// <summary>UI thread: a fresh snapshot, announced when it differs from the last one announced.</summary>
        private void Raise()
        {
            var view = Capture();
            _view = view;
            if (view.Equals(_raised) || _disposed) return;
            _raised = view;
            StateChanged?.Invoke();
        }

        private SessionView Capture()
        {
            lock (_gate)
            {
                var s = _session;
                if (s == null) return SessionView.Idle;
                var queued = _slot.Queued;
                if (queued != null) return SessionView.WaitingFor(queued.Route, queued.Udid);
                var phase = s.Machine.Phase;
                // Failed is final and its failure is set with it: read after the phase, the two always match
                var failure = phase == SessionPhase.Failed ? s.Machine.Failure : SessionFailure.None;
                return SessionView.Of(phase, failure, s.Route, s.Udid, Volatile.Read(ref s.ServingPort), s.LastStopKilled);
            }
        }

        // ---- helpers -----------------------------------------------------------------------

        private static void LogStart(Session s) =>
            Log(s, "session start: " + s.Route + ", device …" + s.Udid.Substring(s.Udid.Length - 4));

        /// <summary>Classified states only: never a full UDID, keys, clipboard text or Python's own output.</summary>
        private static void Log(Session s, string line) => Write("[coredevice] #" + s.Generation + " " + line);

        /// <summary>coredevice.log, opened per line like the tunnel's and the setup's writers of the same file.</summary>
        private static void Write(string line)
        {
            try
            {
                using (var log = new RotatingLog(AppPaths.CoreDeviceLog, 512 * 1024)) log.WriteLine(line);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // a log that cannot be written never fails a session
            }
        }

        private static int FreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>One session's state beside its machine. Fields are shared between pool threads as commented.</summary>
        private sealed class Session
        {
            public Session(int generation, SessionRequest request)
            {
                Generation = generation;
                Udid = request.Udid;
                Route = request.Route;
                Machine = new SessionMachine(request.Route);
            }

            public int Generation { get; }
            public string Udid { get; }
            public CoreDeviceRoute Route { get; }
            public SessionMachine Machine { get; }

            /// <summary>Cancelled when the teardown begins: the tunnel's start, the mount's answer, the cable watch.</summary>
            public CancellationTokenSource Cts { get; } = new CancellationTokenSource();

            public TaskCompletionSource<bool> Stopped { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Guards <see cref="Run"/>, and a serve-web start against a stop.</summary>
            public readonly object RunGate = new object();

            /// <summary>The serve-web job, at most one at a time; under RunGate.</summary>
            public ServeWebRun Run;

            /// <summary>The ready serve-web's port (Volatile/Interlocked); the view shows it only while Ready or Reconnecting.</summary>
            public int ServingPort;

            public volatile bool LastStopKilled;

            /// <summary>1 once the teardown started (Interlocked).</summary>
            public int TearingDown;

            /// <summary>1 while a cable return watcher runs (Interlocked).</summary>
            public int WatchingCable;

            public ServeWebRun CurrentRun()
            {
                lock (RunGate) return Run;
            }

            public void ClearRun(ServeWebRun run)
            {
                lock (RunGate)
                {
                    if (Run == run) Run = null;
                }
            }
        }

        /// <summary>One serve-web process tree: its job, its port, its one stop. The job handle is used only while open.</summary>
        private sealed class ServeWebRun
        {
            private readonly object _gate = new object();
            private bool _disposed;
            private Task _stop;
            private volatile bool _ready;

            public ServeWebRun(CoreDeviceProcessHost host, int port)
            {
                Host = host;
                Port = port;
            }

            public CoreDeviceProcessHost Host { get; }
            public int Port { get; }

            /// <summary>Cancelled when this serve-web is being stopped: its readiness wait and its watchers end.</summary>
            public CancellationTokenSource Cts { get; } = new CancellationTokenSource();

            /// <summary>The readiness wait; set under Session.RunGate right after the start.</summary>
            public volatile Task Readiness = Task.CompletedTask;

            public bool IsReady => _ready;

            public void MarkReady() => _ready = true;

            /// <summary>Whether the job is empty; null once its handle is closed.</summary>
            public bool? IsEmpty()
            {
                lock (_gate) return _disposed ? (bool?)null : Host.IsEmpty;
            }

            public Task StopOnce(Func<Task> stop)
            {
                lock (_gate) return _stop ?? (_stop = Task.Run(stop));
            }

            /// <summary>Closes the job (kill-on-close: anything still in it dies).</summary>
            public void Dispose()
            {
                lock (_gate)
                {
                    if (_disposed) return;
                    _disposed = true;
                    Host.Dispose();
                }
            }
        }
    }
}

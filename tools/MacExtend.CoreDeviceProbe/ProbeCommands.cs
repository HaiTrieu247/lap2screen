using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;

namespace MacExtend.CoreDeviceProbe
{
    internal sealed class ProbeSettings
    {
        public static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(120);

        // serve-web's own cleanup budget is about 3 s per step plus 6 s for stop_all_streams; 40 s leaves room
        // for a slow device without cutting the step that frees the camera and microphone
        public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(40);

        public static readonly TimeSpan ReleaseDeadline = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan VersionCheckTimeout = TimeSpan.FromSeconds(60);

        public CoreDeviceOptions Options;
        public TimeSpan StartupTimeout = DefaultStartupTimeout;
        public TimeSpan Grace = DefaultGrace;

        // smoke-test hooks (tools/coredevice/test-probe.ps1); refused unless a stand-in script is given
        public string TestServeScript;
        public AssignFailure TestFailAssign;
    }

    /// <summary>Which child the --test-fail-assign hook keeps out of its job.</summary>
    internal enum AssignFailure
    {
        None,
        VersionCheck,
        ServeWeb,
    }

    internal enum SessionState
    {
        Starting,
        Ready,
        Stopping,
        Stopped,
    }

    internal static class ExitCodes
    {
        public const int Stopped = 0;
        public const int Usage = 1;
        public const int StartFailed = 2;
        public const int Killed = 3;
        public const int ExitedOnItsOwn = 4;
    }

    /// <summary>
    /// One serve-web run: version check, port check, start, readiness, input, stop. Every way out (stop command,
    /// Ctrl+C, EOF, startup failure or timeout, serve-web exiting, dispose) goes through <see cref="StopAsync"/>,
    /// which runs once.
    /// </summary>
    internal sealed class ProbeSession : IDisposable
    {
        private readonly ProbeSettings _settings;
        private readonly string _helper;
        private readonly CoreDeviceProcessHost _host = new CoreDeviceProcessHost();
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private readonly object _lock = new object();
        private CoreDeviceClient _client;
        private SessionState _state = SessionState.Starting;
        private Task _stop;
        private bool _startFailed;
        private int _startCode = ExitCodes.StartFailed;
        private bool _exitedOnItsOwn;
        private bool _stoppedCleanly;

        public ProbeSession(ProbeSettings settings, string helperExecutable)
        {
            _settings = settings;
            _helper = helperExecutable;
            _host.StatusLine += OnStatusLine;
        }

        public static void Out(string message) => Console.Out.WriteLine("[probe] " + message);

        public SessionState State
        {
            get { lock (_lock) return _state; }
        }

        public CoreDeviceClient Client => _client;

        /// <summary>Cancelled when the stop begins: running commands end and release what they hold.</summary>
        public CancellationToken StopToken => _stopping.Token;

        public string ViewerUrl => _settings.Options.ViewerUrl;

        public async Task<bool> StartAsync()
        {
            var options = _settings.Options;
            var env = CoreDeviceArguments.Environment(Environment.GetEnvironmentVariables(), options);
            try
            {
                Out($"checking pymobiledevice3 in {options.PythonPath}");
                var version = CoreDeviceProcessHost.RunToCompletion(options.PythonPath, CoreDeviceArguments.VersionCheck(), env, ProbeSettings.VersionCheckTimeout,
                    _settings.TestFailAssign == AssignFailure.VersionCheck, pid => Out($"version check pid={pid}")).Trim();
                if (version != CoreDeviceOptions.RequiredVersion)
                {
                    var shown = System.Text.RegularExpressions.Regex.IsMatch(version, @"\A[0-9A-Za-z.+-]{1,32}\z") ? version : "an unrecognised value";
                    Out($"error: this interpreter has pymobiledevice3 {shown}; the probe needs exactly {CoreDeviceOptions.RequiredVersion} (run tools/coredevice/setup.ps1)");
                    return await FailStartAsync("wrong pymobiledevice3 version", ExitCodes.Usage).ConfigureAwait(false);
                }
                Out($"pymobiledevice3 {version}");
            }
            catch (CoreDeviceHostException e)
            {
                Out("error: version check failed: " + e.Message);
                return await FailStartAsync("version check failed", ExitCodes.Usage).ConfigureAwait(false);
            }

            if (_stopping.IsCancellationRequested) return await FailStartAsync("stopped before start", ExitCodes.StartFailed).ConfigureAwait(false);
            if (!PortIsFree(options.HttpPort))
            {
                Out($"error: port {options.HttpPort} is already in use; the probe never talks to a server it did not start (pick another with --port)");
                return await FailStartAsync("port in use", ExitCodes.StartFailed).ConfigureAwait(false);
            }

            var args = CoreDeviceArguments.ServeWeb(options, _settings.TestServeScript);
            Out($"starting serve-web: transport={options.Transport.ToString().ToLowerInvariant()} port={options.HttpPort}");
            try
            {
                _host.Start(options.PythonPath, args, env, _settings.TestFailAssign == AssignFailure.ServeWeb, pid => Out($"child pid={pid}"));
            }
            catch (CoreDeviceHostException e)
            {
                Out("error: " + e.Message + "; the child was terminated before it ran");
                return await FailStartAsync("start failed", ExitCodes.StartFailed).ConfigureAwait(false);
            }

            var failure = await _host.WaitReadyAsync(options.HttpPort, _settings.StartupTimeout, _stopping.Token, Out).ConfigureAwait(false);
            switch (failure)
            {
                case ReadyFailure.None:
                    break;
                case ReadyFailure.ExitedEarly:
                    _host.WaitForOutput(TimeSpan.FromSeconds(2));
                    Out("error: serve-web exited during startup: " + Explain(_host.LastFatal));
                    return await FailStartAsync("serve-web exited during startup", ExitCodes.StartFailed).ConfigureAwait(false);
                case ReadyFailure.ForeignListener:
                    return await FailStartAsync("port taken by another process", ExitCodes.StartFailed).ConfigureAwait(false);
                case ReadyFailure.TimedOut:
                    Out($"error: serve-web was not ready within {_settings.StartupTimeout.TotalSeconds:0} s");
                    return await FailStartAsync("startup timeout", ExitCodes.StartFailed).ConfigureAwait(false);
                default:
                    return await FailStartAsync("stopped during startup", ExitCodes.StartFailed).ConfigureAwait(false);
            }

            lock (_lock)
            {
                if (_stop != null)
                {
                    // a Ctrl+C landed just as the checks passed
                    _startFailed = true;
                    return false;
                }
                _client = new CoreDeviceClient(options.HttpPort);
                _state = SessionState.Ready;
            }
            Out($"ready url={ViewerUrl}");
            Out("ready means: serve-web runs, listens on loopback and serves the viewer page. It does not mean the video decodes or that input reaches the device; check that on the device.");
            _ = Task.Run(WatchExitAsync);
            return true;
        }

        public Task StopAsync(string reason)
        {
            lock (_lock)
            {
                if (_stop != null)
                {
                    if (_state == SessionState.Stopping) Out("already stopping");
                    return _stop;
                }
                if (_state == SessionState.Starting && !_startFailed)
                {
                    // stopped (Ctrl+C, EOF) before it was ready
                    _startFailed = true;
                    _startCode = ExitCodes.StartFailed;
                }
                _state = SessionState.Stopping;
                _stop = Task.Run(() => StopCoreAsync(reason));
            }
            _stopping.Cancel();
            return _stop;
        }

        public bool IsStopping
        {
            get { lock (_lock) return _stop != null; }
        }

        /// <summary>Read once the stop has finished.</summary>
        public int ExitCode
        {
            get
            {
                lock (_lock)
                {
                    if (_startFailed) return _startCode;
                    if (!_stoppedCleanly) return ExitCodes.Killed;
                    return _exitedOnItsOwn ? ExitCodes.ExitedOnItsOwn : ExitCodes.Stopped;
                }
            }
        }

        public void PrintStatus()
        {
            var pids = SafeJobPids();
            var client = _client;
            Out(string.Join(" ", new[]
            {
                "status",
                "state=" + State,
                "port=" + _settings.Options.HttpPort,
                "transport=" + _settings.Options.Transport.ToString().ToLowerInvariant(),
                "job_pids=" + (pids.Count == 0 ? "none" : string.Join(",", pids)),
                "codec=" + (_host.CodecString ?? "unknown"),
                "input_open=" + (client != null && client.IsOpen),
                "contact_may_be_held=" + (client != null && client.ContactMayBeHeld),
                "keys_may_be_held=" + (client != null && client.KeysMayBeHeld),
            }));
        }

        public void Dispose()
        {
            StopAsync("probe closing").Wait();
            _client?.Dispose();
            _host.Dispose();
            _stopping.Dispose();
        }

        private async Task<bool> FailStartAsync(string reason, int code)
        {
            lock (_lock)
            {
                if (!_startFailed)
                {
                    _startFailed = true;
                    _startCode = code;
                }
            }
            await StopAsync(reason).ConfigureAwait(false);
            return false;
        }

        private async Task StopCoreAsync(string reason)
        {
            Out("stopping: " + reason);
            var client = _client;
            if (client != null)
            {
                client.Close();
                var outcome = await client.ReleaseAllAsync(ProbeSettings.ReleaseDeadline).ConfigureAwait(false);
                if (outcome != ReleaseOutcome.NothingHeld) Out("probe input release: " + outcome.ToString().ToLowerInvariant());
            }

            var exit = await _host.StopAsync(_helper, _settings.Grace, Out).ConfigureAwait(false);
            if (_host.RootPid == 0)
            {
                Out("stopped (serve-web was never started)");
                lock (_lock)
                {
                    _stoppedCleanly = true;
                    _state = SessionState.Stopped;
                }
                return;
            }
            var jobEmpty = _host.WaitEmpty(TimeSpan.FromSeconds(5));
            var portFree = WaitPortFree(_settings.Options.HttpPort, TimeSpan.FromSeconds(5));

            string cleanup;
            if (exit == StopExit.Killed) cleanup = "unconfirmed (serve-web was terminated)";
            else if (_host.CleanupReportedComplete) cleanup = "reported-complete";
            else if (_host.FailedCleanupStep != null) cleanup = "incomplete (" + _host.FailedCleanupStep + " failed or timed out)";
            else cleanup = "not-reported";

            // only now, with every process gone and the port free, is the session called stopped
            if (jobEmpty && portFree) Out($"stopped exit={exit.ToString().ToLowerInvariant()} cleanup={cleanup} port=released");
            else Out($"stop incomplete: job_empty={jobEmpty} port_released={portFree}");
            // serve-web opens the device's media session before it listens, so this applies to every run
            Out("a stopped process is not proof the device released camera and microphone: run get-media-stream-server-status, then open Camera and Voice Memos on the device (docs/TESTING.md, CoreDevice).");

            lock (_lock)
            {
                _stoppedCleanly = exit != StopExit.Killed && jobEmpty && portFree;
                _state = SessionState.Stopped;
            }
        }

        private async Task WatchExitAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(500, _stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                bool empty;
                try
                {
                    empty = _host.IsEmpty;
                }
                catch (CoreDeviceHostException)
                {
                    return;
                }
                if (!empty) continue;
                lock (_lock)
                {
                    if (_stop != null) return;
                    _exitedOnItsOwn = true;
                }
                _host.WaitForOutput(TimeSpan.FromSeconds(2));
                Out("serve-web exited on its own: " + Explain(_host.LastFatal));
                await StopAsync("serve-web exited").ConfigureAwait(false);
                return;
            }
        }

        private void OnStatusLine(ServeWebLogLine line)
        {
            switch (line.Kind)
            {
                case ServeWebLogKind.Listening:
                    Out($"serve-web listening on port {line.Port}");
                    break;
                case ServeWebLogKind.CodecString:
                    Out("video codec " + line.Detail + " (the browser still has to decode it: check the viewer)");
                    break;
                case ServeWebLogKind.HidWorkerStarted:
                    Out("serve-web HID worker started");
                    break;
                case ServeWebLogKind.StreamStartDelayed:
                    Out("serve-web: the device stream did not start within 25 s; it retries when the viewer asks");
                    break;
                case ServeWebLogKind.DeviceDisconnected:
                    Out("serve-web: device disconnected; it waits for tunneld to list it again (with --userspace it never comes back: stop and start again)");
                    break;
                case ServeWebLogKind.DeviceReconnected:
                    Out("serve-web: device back via tunneld; the probe does not replay any input");
                    break;
                case ServeWebLogKind.ShutdownStepFailed:
                    Out("serve-web cleanup step failed or timed out: " + (line.Detail ?? "unknown step") + (line.Detail == "stop_all_streams" ? " (this is the step that frees camera and microphone)" : ""));
                    break;
                case ServeWebLogKind.ShutdownComplete:
                    Out("serve-web reported shutdown complete");
                    break;
                case ServeWebLogKind.MediaInUse:
                case ServeWebLogKind.ServiceUnavailable:
                case ServeWebLogKind.DeviceNotFound:
                case ServeWebLogKind.Error:
                    Out("serve-web error: " + Explain(line.Kind));
                    break;
            }
        }

        private static string Explain(ServeWebLogKind kind)
        {
            switch (kind)
            {
                case ServeWebLogKind.MediaInUse:
                    return "the device's camera or microphone is in use (Camera, Voice Memos, a call); close that app and start again";
                case ServeWebLogKind.ServiceUnavailable:
                    return "the device does not offer the developer service: check Developer Mode and mount the Developer Disk Image (tools/coredevice/README.md)";
                case ServeWebLogKind.DeviceNotFound:
                    return "device not found: check the cable, the trust prompt, the UDID and, for --transport tunnel, that tunneld runs";
                case ServeWebLogKind.Error:
                    return "an error the probe does not classify; run the upstream command from tools/coredevice/README.md by hand to read it";
                default:
                    return "no error was classified; run the upstream command from tools/coredevice/README.md by hand to read its output";
            }
        }

        private IReadOnlyList<int> SafeJobPids()
        {
            try
            {
                return _host.JobProcessIds();
            }
            catch (CoreDeviceHostException)
            {
                return new int[0];
            }
            catch (ObjectDisposedException)
            {
                return new int[0];
            }
        }

        private static bool PortIsFree(int port)
        {
            if (!TcpListeners.IsFree(port)) return false;
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
            try
            {
                probe.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                probe.Stop();
            }
        }

        private static bool WaitPortFree(int port, TimeSpan timeout)
        {
            var watch = Stopwatch.StartNew();
            while (!TcpListeners.IsFree(port))
            {
                if (watch.Elapsed >= timeout) return false;
                Thread.Sleep(100);
            }
            return true;
        }
    }

    /// <summary>
    /// The API input commands. They send HID coordinates (0..65535) in the device's portrait orientation; rotation
    /// is tested through the viewer. Hold, drag and key always end with a release, also when interrupted. Hold and
    /// drag run on the clock under a deadline of MS + 1 s (<see cref="CoreDeviceGestures"/>), so 10 s at most.
    /// </summary>
    internal static class ProbeCommands
    {
        private static readonly int MaxDurationMs = (int)CoreDeviceGestures.MaxDuration.TotalMilliseconds;
        private const int DefaultHoldMs = 1000;
        private const int DefaultDragMs = 600;
        private const int KeyPressMs = 100;

        public const string Usage =
            "  tap X Y                      one tap (serve-web's own contact, 50 ms, release)\n" +
            "  hold X Y [MS]                press and hold, default 1000 ms, at most 9000\n" +
            "  drag X1 Y1 X2 Y2 [MS]        press, move in a straight line, lift; default 600 ms, at most 9000\n" +
            "                               (hold and drag are cut, and the finger lifted, if still running 1 s past MS)\n" +
            "  key USAGE [USAGE ...]        press HID keyboard usages together for 100 ms (decimal or 0x hex, 0x04-0xE7)\n" +
            "  release                      lift any finger and release every key, whatever the probe thinks is held\n" +
            "  home                         press the Home button\n" +
            "  X and Y are HID coordinates 0..65535 across the device screen in portrait; 32768 32768 is the centre.";

        public static async Task RunAsync(ProbeSession session, string command, string[] args)
        {
            var client = session.Client;
            var token = session.StopToken;
            if (session.State != SessionState.Ready || client == null)
            {
                ProbeSession.Out("error: input is only accepted while serve-web is ready");
                return;
            }
            try
            {
                switch (command)
                {
                    case "tap":
                        Need(args, 2, 2);
                        await client.TapAsync(Coordinate(args[0]), Coordinate(args[1]), token).ConfigureAwait(false);
                        ProbeSession.Out("sent tap");
                        break;
                    case "hold":
                        Need(args, 2, 3);
                        Report("hold", client, await CoreDeviceGestures.HoldAsync(client, Coordinate(args[0]), Coordinate(args[1]),
                            Duration(args, 2, DefaultHoldMs), ProbeSettings.ReleaseDeadline, token).ConfigureAwait(false));
                        break;
                    case "drag":
                        Need(args, 4, 5);
                        Report("drag", client, await CoreDeviceGestures.DragAsync(client, Coordinate(args[0]), Coordinate(args[1]), Coordinate(args[2]), Coordinate(args[3]),
                            Duration(args, 4, DefaultDragMs), ProbeSettings.ReleaseDeadline, token).ConfigureAwait(false));
                        break;
                    case "key":
                        if (args.Length < 1) throw new FormatException("key needs at least one usage");
                        var usages = args.Select(ParseUsage).ToList();
                        await KeyAsync(client, usages, token).ConfigureAwait(false);
                        // the usages themselves are not echoed: the probe keeps no record of what was typed
                        ProbeSession.Out($"sent key press ({usages.Count} usage{(usages.Count == 1 ? "" : "s")})");
                        break;
                    case "release":
                        Need(args, 0, 0);
                        ProbeSession.Out("release: " + (await client.ReleaseAllAsync(ProbeSettings.ReleaseDeadline, force: true).ConfigureAwait(false)).ToString().ToLowerInvariant());
                        break;
                    case "home":
                        Need(args, 0, 0);
                        await client.HomeAsync(token).ConfigureAwait(false);
                        ProbeSession.Out("sent home");
                        break;
                }
            }
            catch (FormatException e)
            {
                ProbeSession.Out("error: " + e.Message);
            }
            catch (ArgumentException e)
            {
                ProbeSession.Out("error: " + e.Message.Split('\n')[0].Split('\r')[0]);
            }
            catch (OperationCanceledException)
            {
                ProbeSession.Out("input interrupted by the stop");
            }
            catch (CoreDeviceClientException e)
            {
                ProbeSession.Out("error: " + e.Message + ". No retry; the probe takes no more input in this session (stop, then start it again).");
            }
        }

        public static bool IsInputCommand(string command) =>
            command == "tap" || command == "hold" || command == "drag" || command == "key" || command == "release" || command == "home";

        private static void Report(string gesture, CoreDeviceClient client, GestureResult result)
        {
            if (result.End == GestureEnd.Completed)
            {
                ProbeSession.Out("sent " + gesture);
                return;
            }
            ProbeSession.Out($"{gesture} cut at its deadline (MS + 1 s): serve-web answered too slowly; release: {result.Cleanup.ToString().ToLowerInvariant()}"
                + (client.IsOpen ? "" : ". No retry; the probe takes no more input in this session (stop, then start it again)."));
        }

        private static async Task KeyAsync(CoreDeviceClient client, IReadOnlyCollection<int> usages, CancellationToken token)
        {
            try
            {
                await client.SetKeysAsync(usages, token).ConfigureAwait(false);
                await Task.Delay(KeyPressMs, token).ConfigureAwait(false);
                await client.SetKeysAsync(new int[0], token).ConfigureAwait(false);
            }
            finally
            {
                if (client.KeysMayBeHeld) await client.ReleaseAllAsync(ProbeSettings.ReleaseDeadline).ConfigureAwait(false);
            }
        }

        private static void Need(string[] args, int min, int max)
        {
            if (args.Length < min || args.Length > max) throw new FormatException("wrong number of arguments; type help");
        }

        private static int Coordinate(string text)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > CoreDeviceClient.MaxCoordinate)
            {
                throw new FormatException("coordinates are whole numbers from 0 to 65535");
            }
            return value;
        }

        private static TimeSpan Duration(string[] args, int index, int fallback)
        {
            if (args.Length <= index) return TimeSpan.FromMilliseconds(fallback);
            if (!int.TryParse(args[index], NumberStyles.None, CultureInfo.InvariantCulture, out var ms) || ms < 1 || ms > MaxDurationMs)
            {
                throw new FormatException($"durations are 1 to {MaxDurationMs} ms");
            }
            return TimeSpan.FromMilliseconds(ms);
        }

        private static int ParseUsage(string text)
        {
            int value;
            var ok = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)
                : int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
            if (!ok) throw new FormatException("key usages are decimal or 0x hex numbers");
            return value;
        }
    }
}

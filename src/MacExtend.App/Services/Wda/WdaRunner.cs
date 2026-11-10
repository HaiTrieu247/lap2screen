using System;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;
using MacExtend.Core.Wda;
using MacExtend.CoreDeviceHost;

namespace MacExtend.Services.Wda
{
    /// <summary>
    /// The XCUITest runner (pymobiledevice3 developer dvt xcuitest) that launches WDA on the device and keeps it alive,
    /// in a kill-on-close job of its own: it never outlives the app. Its lines are classified (WdaLog) and dropped, since
    /// they carry the UDID and the device's name. Stopping sends one Ctrl+C, like serve-web's stop, then kills the job.
    /// </summary>
    internal sealed class WdaRunner : IDisposable
    {
        /// <summary>The runner has nothing on the device to clean up beyond the test session: a short grace is enough.</summary>
        private static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

        private readonly CoreDeviceProcessHost _host = new CoreDeviceProcessHost();
        private readonly Action<string> _log;
        private readonly object _lock = new object();
        private WdaFailure _failure = WdaFailure.None;

        public WdaRunner(Action<string> log)
        {
            _log = log ?? (_ => { });
        }

        /// <summary>The most telling failure the runner has written so far; None while it reported nothing wrong.</summary>
        public WdaFailure Failure
        {
            get { lock (_lock) return _failure; }
        }

        /// <summary>Every process of the job has exited.</summary>
        public bool HasExited => _host.IsEmpty;

        /// <exception cref="CoreDeviceHostException">python.exe could not be started in a job.</exception>
        public void Start(string udid, string bundleId)
        {
            _host.RawLineHandler = OnLine;
            _host.Start(AppPaths.CoreDevicePython, WdaArguments.XcuiTest(bundleId),
                        CoreDeviceArguments.CommandEnvironment(Environment.GetEnvironmentVariables(), udid), false,
                        pid => _log("runner started (pid " + pid + ")"));
        }

        /// <summary>Lets the readers take in an exited runner's last lines before its failure is read.</summary>
        public void SettleOutput() => _host.WaitForOutput(TimeSpan.FromSeconds(2));

        public async Task StopAsync()
        {
            var exit = await _host.StopAsync(AppPaths.SelfExe, Grace, line => _log("runner stop: " + line.Replace("serve-web", "runner"))).ConfigureAwait(false);
            _log("runner stopped (" + exit + ")");
        }

        public void Dispose() => _host.Dispose();

        private void OnLine(string line)
        {
            var failure = WdaLog.Classify(line);
            if (failure == WdaFailure.None) return;
            bool changed;
            lock (_lock)
            {
                var next = WdaLog.Pick(_failure, failure);
                changed = next != _failure;
                _failure = next;
            }
            if (changed) _log("runner reported " + failure);
        }
    }
}

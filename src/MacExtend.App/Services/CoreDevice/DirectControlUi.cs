using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.CoreDeviceHost;
using MacExtend.ViewModels;
using MacExtend.Views;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>
    /// Opens a session from the tray or the settings page: the setup window first while nothing could work yet, then the
    /// device (the last one, the only one, or the user's pick, DirectOpen), then the session and its window. One open at
    /// a time; everything here runs on the UI thread, the device queries on the thread pool.
    /// </summary>
    internal sealed class DirectControlUi
    {
        // PowerShellRunner has no deadline of its own (Get-NetFirewallRule, Get-ScheduledTask)
        private static readonly TimeSpan WindowsCheckTimeout = TimeSpan.FromSeconds(30);

        private readonly CoreDeviceService _service;
        private readonly SettingsService _settings;
        private CoreDeviceWindow _window;
        private CoreDeviceSetupWindow _setup;
        // the picker or a prompt while one is open (ShowDialog)
        private Window _dialog;
        private bool _opening;
        private bool _exiting;

        public DirectControlUi(CoreDeviceService service, SettingsService settings)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// <see cref="IsOpening"/> changed, or the setup window closed (N20: what it set up shows on the settings page).
        /// Always on the UI thread.
        /// </summary>
        public event Action Changed;

        /// <summary>The devices are being looked at for a new session.</summary>
        public bool IsOpening => _opening;

        /// <summary>The tray's item: stops the session, or opens one.</summary>
        public void Toggle()
        {
            if (_service.IsActive) _ = _service.StopAsync();
            else _ = OpenAsync();
        }

        public async Task OpenAsync()
        {
            if (_exiting || _opening) return;
            // a session runs: its window comes forward. One that is stopping takes a new open, which waits for that stop
            if (_service.IsActive && _service.Phase != SessionPhase.Stopping)
            {
                ShowWindow();
                return;
            }
            SetOpening(true);
            try
            {
                await OpenCoreAsync();
            }
            catch (Exception e)
            {
                // a bug or a system failure: logged by type, never left as an unobserved task
                Log("open failed: unexpected " + e.GetType().Name);
            }
            finally
            {
                SetOpening(false);
            }
        }

        public void OpenSetup()
        {
            if (_exiting) return;
            if (_setup != null)
            {
                Bring(_setup);
                return;
            }
            var setup = new CoreDeviceSetupWindow(new CoreDeviceSetupViewModel(_settings, _service));
            setup.Closed += (_, __) =>
            {
                if (_setup == setup) _setup = null;
                if (_window != null) _window.SetupOpen = false;
                Changed?.Invoke();
            };
            _setup = setup;
            // a pinned direct-control window would stay over it
            if (_window != null) _window.SetupOpen = true;
            setup.Show();
        }

        /// <summary>
        /// App exit (N5): nothing opens any more, and the windows close. The caller awaits the session's stop; the
        /// direct-control window starts it as it closes.
        /// </summary>
        public void CloseForExit()
        {
            _exiting = true;
            _dialog?.Close();
            _setup?.Close();
            _window?.Close();
        }

        private async Task OpenCoreAsync()
        {
            while (true)
            {
                var plan = await PlanAsync();
                if (_exiting) return;
                switch (plan.Step)
                {
                    case DirectOpenStep.Setup:
                        Log("open: setup first");
                        OpenSetup();
                        return;
                    case DirectOpenStep.Problem:
                        Log("open: " + plan.Failure);
                        // N12: what the setup can fix (Windows' part of Wi-Fi) is offered right here; the devices are looked at again after it
                        if (!ShowProblem(plan.Failure) || _exiting) return;
                        continue;
                    case DirectOpenStep.Pick:
                        Log("open: " + plan.Choices.Count + " devices, the user picks");
                        var choice = Pick(plan.Choices);
                        if (choice == null || _exiting) return;
                        Start(choice.Udid, choice.Route);
                        return;
                    default:
                        Start(plan.Udid, plan.Route);
                        return;
                }
            }
        }

        private async Task<DirectOpenPlan> PlanAsync()
        {
            // synchronous, up to 500 ms
            var appleService = await Task.Run(() => CoreDeviceCommands.AppleServiceReachable());
            var configured = _settings.Current.WifiDevices();
            if (DirectOpen.NeedsSetupFirst(CoreDeviceRuntime.IsCurrent, appleService, configured.Count)) return DirectOpenPlan.Setup;
            var usb = appleService ? await UsbDevicesAsync() : new string[0];
            var wifi = configured.Where(CoreDeviceCommands.RemotePaired).ToList();
            var last = _settings.Current.DirectLastDevice;
            // the PowerShell check only when its answer could change what opens
            var windowsDone = !DirectOpen.NeedsWindowsCheck(usb, wifi, last) || await WindowsSetupDoneAsync();
            return DirectOpen.Plan(usb, wifi, windowsDone, last);
        }

        private static async Task<IReadOnlyList<string>> UsbDevicesAsync()
        {
            try
            {
                return await CoreDeviceCommands.UsbDevicesAsync();
            }
            catch (CoreDeviceHostException)
            {
                // the runtime's python.exe could not be started in a job: no device on the cable as far as we can tell
                Log("open: the cable list could not run");
                return new string[0];
            }
        }

        /// <summary>
        /// Windows' part of Wi-Fi. No answer in time counts as in place: the session then tries Wi-Fi and says itself
        /// what is missing, instead of a prompt to set up what may well be there.
        /// </summary>
        private static async Task<bool> WindowsSetupDoneAsync()
        {
            var check = Task.Run(() => WindowsSetupState.IsDoneAsync());
            if (await Task.WhenAny(check, Task.Delay(WindowsCheckTimeout)) != check)
            {
                // it goes on until PowerShell exits; whatever it ends with must not reach the crash log
                _ = check.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                Log("open: no answer from PowerShell about Windows' part of Wi-Fi");
                return true;
            }
            return await check;
        }

        private void Start(string udid, CoreDeviceRoute route)
        {
            Log("open: device …" + udid.Substring(udid.Length - 4) + " " + route);
            _settings.Current.DirectLastDevice = udid;
            _settings.ScheduleSave();
            // before the window: it closes itself on an idle session once it has loaded
            _service.Start(udid, route);
            ShowWindow();
        }

        private void ShowWindow()
        {
            if (_window != null)
            {
                Bring(_window);
                return;
            }
            var window = new CoreDeviceWindow(_service, _settings, OpenSetup, () => _ = RetryAsync()) { SetupOpen = _setup != null };
            window.Closed += (_, __) =>
            {
                if (_window == window) _window = null;
            };
            _window = window;
            window.Show();
        }

        /// <summary>"Thử lại" in the window after a failure: the same window shows the new session.</summary>
        private async Task RetryAsync()
        {
            await _service.StopAsync();
            await OpenAsync();
        }

        private DeviceChoice Pick(IReadOnlyList<DeviceChoice> choices)
        {
            var picker = new DevicePickerWindow(choices);
            return ShowDialog(picker) ? picker.Chosen : null;
        }

        /// <summary>
        /// The open cannot start a session. A failure the setup can fix gets "Thiết lập lại": the one-time setup again
        /// (one UAC), through the app's setup gate.
        /// </summary>
        /// <returns>true when the setup ran and the devices should be looked at again.</returns>
        private bool ShowProblem(SessionFailure failure)
        {
            var fixable = CoreDeviceMessages.NeedsSetup(failure);
            var prompt = new DirectPromptWindow(DirectControlTexts.ProblemTitle(failure), DirectControlTexts.ProblemDetail(failure),
                                                fixable ? DirectControlTexts.Resetup : null, fixable ? (Func<Action<string>, Task<bool>>)ResetupAsync : null);
            return ShowDialog(prompt);
        }

        /// <summary>The prompt's "Thiết lập lại": --coredevice-setup again with the installed runtime, behind one UAC.</summary>
        private static async Task<bool> ResetupAsync(Action<string> say)
        {
            var gate = CoreDeviceRuntime.SetupGate;
            var owner = new object();
            var message = SetupMessages.Installing(true);
            if (!gate.TryEnter(owner, SetupStepId.Components, message))
            {
                say(DirectControlTexts.SetupBusy);
                return false;
            }
            try
            {
                say(message);
                // its outcome is logged by SetupAsync itself
                var outcome = await CoreDeviceRuntime.SetupAsync(null);
                say(SetupMessages.Installed(outcome));
                return SetupMessages.IsOk(outcome);
            }
            finally
            {
                gate.Exit(owner);
            }
        }

        private bool ShowDialog(Window dialog)
        {
            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible && w != dialog);
            if (owner != null)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            _dialog = dialog;
            try
            {
                return dialog.ShowDialog() == true;
            }
            finally
            {
                _dialog = null;
            }
        }

        /// <summary>A minimized window is restored first, to maximized again if it was (SC_RESTORE, not WindowState.Normal).</summary>
        private static void Bring(Window window)
        {
            if (window.WindowState == WindowState.Minimized) SystemCommands.RestoreWindow(window);
            window.Activate();
        }

        private void SetOpening(bool opening)
        {
            if (_opening == opening) return;
            _opening = opening;
            Changed?.Invoke();
        }

        /// <summary>coredevice.log, opened per line like the session's writer of the same file. Never a full UDID.</summary>
        private static void Log(string line)
        {
            try
            {
                using (var log = new RotatingLog(AppPaths.CoreDeviceLog, 512 * 1024)) log.WriteLine("[direct] " + line);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // a log that cannot be written never stops an open
            }
        }
    }
}

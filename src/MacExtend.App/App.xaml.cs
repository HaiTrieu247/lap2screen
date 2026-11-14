using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MacExtend.Native;
using MacExtend.Services;
using MacExtend.Services.Bluetooth;
using MacExtend.Services.CoreDevice;
using MacExtend.Themes;
using MacExtend.Tray;
using MacExtend.ViewModels;
using MacExtend.Views;

namespace MacExtend
{
    public partial class App : Application
    {
        private const string InstanceName = @"Local\MacExtend.SingleInstance";
        private const string ShowEventName = @"Local\MacExtend.Show";
        // N5: exit waits this long for the direct-control session's stop: touch released, serve-web's cleanup (up to 40 s),
        // then a tunneld the session started. After that the job objects end serve-web with the process.
        private static readonly TimeSpan DirectStopBound = TimeSpan.FromSeconds(70);

        private Mutex _singleInstance;
        private EventWaitHandle _showRequest;
        private RegisteredWaitHandle _showWait;
        private SettingsService _settings;
        private ReceiverController _controller;
        private MainViewModel _vm;
        private MainWindow _window;
        private TrayIcon _tray;
        private AirPlayPresenter _presenter;
        private BluetoothController _bluetooth;
        // one direct-control service (and its Wi-Fi TunnelController) for the app's lifetime
        private CoreDeviceService _direct;
        // the switch: a session for the iPhone or iPad mirroring, its touches on the AirPlay picture
        private DirectControlController _control;
        private bool _exiting;
        private bool _crashing;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // serve-web's Ctrl+C helper and the elevated CoreDevice setup and removal: no UI, no receiver
            if (Services.CoreDevice.CoreDeviceHelpers.TryRun(e.Args, out var coreDeviceExit))
            {
                Shutdown(coreDeviceExit);
                return;
            }

            // a copy of this exe in %TEMP% applying an update: no UI, no receiver
            if (UpdateHelper.TryRun(e.Args, out var helperExit))
            {
                Shutdown(helperExit);
                return;
            }

            _singleInstance = new Mutex(true, InstanceName, out var first);
            _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            if (!first)
            {
                // already running: bring its window forward instead of starting a second receiver
                _showRequest.Set();
                Shutdown();
                return;
            }

            // inherited by uxplay: no "DLL not found" or crash dialogs from the hidden process
            Win32.SetErrorMode(Win32.SEM_FAILCRITICALERRORS | Win32.SEM_NOGPFAULTERRORBOX);

            AppPaths.EnsureDirectories();
            DispatcherUnhandledException += (_, args) =>
            {
                args.Handled = true;
                Crash(args.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) => Crash(args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                WriteCrashLog(args.Exception);
                args.SetObserved();
            };
            UxPlayHost.StopLeftovers();
            ThemeManager.Initialize(this);

            _settings = new SettingsService(AppPaths.SettingsFile);
            _controller = new ReceiverController(() => _settings.Current, Dispatcher);
            _presenter = new AirPlayPresenter(_controller, () => _settings.Current, Dispatcher);
            _bluetooth = new BluetoothController(() => _settings.Current, _controller, _presenter, Dispatcher);
            _bluetooth.Refresh();
            _direct = new CoreDeviceService(Dispatcher);
            _control = new DirectControlController(_settings, _controller, _presenter, _direct, Dispatcher);
            // a click on a picture that takes touches is a tap, never the start of a Bluetooth session
            _bluetooth.TouchVideo = () => _control.TouchVideo;
            _control.StateChanged += _bluetooth.Refresh;
            _control.Refresh();
            _vm = new MainViewModel(_controller, _bluetooth, _control, ChooseDirectDevice, _settings, ExitForUpdate);
            _vm.SyncAutoStart();
            _controller.RefreshPreview();

            _vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.HideCursor) || args.PropertyName == nameof(MainViewModel.AlwaysOnTop))
                {
                    _presenter.SettingsChanged();
                }
                if (args.PropertyName == nameof(MainViewModel.BluetoothControl)) _bluetooth.Refresh();
                if (args.PropertyName == nameof(MainViewModel.DirectControl)) _control.Refresh();
            };
            _tray = new TrayIcon(_controller, _bluetooth, _control, ChooseDirectDevice, () => _vm.DirectControl, on => _vm.DirectControl = on,
                                 ShowMainWindow, ExitApp, () => _presenter.HasVideoWindow,
                                 () => _presenter.ShowAirPlay(), () => _vm.AlwaysOnTop, onTop => _vm.AlwaysOnTop = onTop);
            _vm.Update.NewVersionFound += version => _tray?.ShowUpdate(version, () =>
            {
                ShowMainWindow();
                if (_vm.Update.InstallCommand.CanExecute(null)) _vm.Update.InstallCommand.Execute(null);
            });
            var afterUpdate = UpdateResultArg(e.Args);
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showRequest,
                (_, __) => Dispatcher.BeginInvoke(new Action(ShowMainWindow)), null, Timeout.Infinite, false);

            if (afterUpdate != null)
            {
                UpdateService.CleanupStaging();
                Core.Updates.UpdateApplier.CleanupLeftovers(AppPaths.InstallDir);
                _vm.Update.ShowResultOfLastUpdate(afterUpdate, e.Args.Length > 1 ? e.Args[1] : "");
            }
            if (afterUpdate != null || !e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)) ShowMainWindow();
            _controller.Start();
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            // Windows is logging off or shutting down: stop uxplay now, the UI thread may not get another turn. The
            // direct-control session is asked to stop (serve-web's Ctrl+C) and runs on without the UI thread.
            _control?.Dispose();
            _direct?.Dispose();
            _controller?.ShutdownNow();
            _settings?.Flush();
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _showWait?.Unregister(null);
            _tray?.Dispose();
            _control?.Dispose();
            _direct?.Dispose();
            _bluetooth?.Dispose();
            _controller?.Dispose();
            _singleInstance?.Dispose();
            base.OnExit(e);
        }

        /// <summary>
        /// Last resort: record what happened, take uxplay down with us and tell the user, instead of
        /// the tray icon silently disappearing while the Mac can still connect.
        /// </summary>
        private void Crash(Exception error)
        {
            if (_crashing) return;
            _crashing = true;
            WriteCrashLog(error);
            try
            {
                // input back to Windows before the dialog below waits for a click; the hooks and the Bluetooth
                // service end with the process
                _bluetooth?.ReleaseInput();
                _controller?.ShutdownNow();
                // asked to stop; serve-web may still get its cleanup while the dialog below waits
                _direct?.Dispose();
                _tray?.Dispose();
            }
            catch (Exception)
            {
                // already failing; the job object still ends uxplay when this process exits
            }
            MessageBox.Show("MacExtend gặp lỗi và phải đóng. Mở lại MacExtend để tiếp tục dùng.\n\nChi tiết: " + CrashLogPath,
                            "MacExtend", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
        }

        private static string CrashLogPath => System.IO.Path.Combine(AppPaths.LogDir, "crash.log");

        private static void WriteCrashLog(Exception error)
        {
            try
            {
                System.IO.File.AppendAllText(CrashLogPath, DateTime.Now.ToString("u") + " " + error + Environment.NewLine);
            }
            catch (Exception)
            {
                // nowhere left to report to
            }
        }

        private static string UpdateResultArg(string[] args)
        {
            if (args.Length == 0) return null;
            var first = args[0];
            return first == UpdateHelper.AfterUpdateArg || first == UpdateHelper.CancelledArg || first == UpdateHelper.FailedArg ? first : null;
        }

        /// <summary>The update is downloaded: hand over to the helper and get out of its way.</summary>
        private async void ExitForUpdate(string stagingDir)
        {
            // the helper waits only 30 s for this process to end, and a new runtime replaces serve-web's files: a
            // direct-control session is stopped first (N5), and none starts until the update is over
            await Task.WhenAny(_control.StopAsync(), Task.Delay(DirectStopBound));
            try
            {
                UpdateHelper.Launch(stagingDir);
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is System.IO.IOException ||
                                          error is UnauthorizedAccessException)
            {
                _vm.Update.ReportLaunchFailure(error.Message);
                _control.Resume();
                return;
            }
            ExitApp();
        }

        /// <summary>Several devices for direct control: the user picks the one mirroring; it is remembered for that AirPlay name.</summary>
        private void ChooseDirectDevice()
        {
            if (_exiting || _control.View.Phase != Core.CoreDevice.DirectPhase.Choose || _control.Choices.Count == 0) return;
            var picker = new Views.DevicePickerWindow(_control.Choices);
            var owner = Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible && w != picker);
            if (owner != null)
            {
                picker.Owner = owner;
                picker.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            if (picker.ShowDialog() == true && picker.Chosen != null) _control.Choose(picker.Chosen.Udid);
        }

        private void ShowMainWindow()
        {
            if (_exiting) return;
            if (_window == null)
            {
                _window = new MainWindow(_vm);
                _window.HiddenToTray += () => _tray.ShowStillRunningHint();
            }
            _window.ShowAndActivate();
        }

        private async void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            _settings.Flush();
            _window?.CloseForExit();
            // N5: the direct-control session stops before the process ends, so serve-web gets its one Ctrl+C and gives
            // the device's camera and microphone back; the setup window closes now
            var directStop = _control.StopAsync();
            var bound = Task.Delay(DirectStopBound);
            if (directStop.IsCompleted)
            {
                _tray.Dispose();
                _tray = null;
            }
            else
            {
                _tray.ShowExiting();
            }
            _bluetooth?.Dispose();
            _presenter?.Dispose();
            await _controller.StopAsync();
            await Task.WhenAny(directStop, bound);
            _control.Dispose();
            _tray?.Dispose();
            _tray = null;
            Shutdown();
        }
    }
}

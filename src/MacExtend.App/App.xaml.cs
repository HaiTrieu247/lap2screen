using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MacExtend.Native;
using MacExtend.Services;
using MacExtend.Services.Bluetooth;
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
            _vm = new MainViewModel(_controller, _bluetooth, _settings, ExitForUpdate);
            _vm.SyncAutoStart();
            _controller.RefreshPreview();

            _vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.HideCursor) || args.PropertyName == nameof(MainViewModel.AlwaysOnTop))
                {
                    _presenter.SettingsChanged();
                }
                if (args.PropertyName == nameof(MainViewModel.BluetoothControl)) _bluetooth.Refresh();
            };
            _tray = new TrayIcon(_controller, _bluetooth, ShowMainWindow, ExitApp, () => _presenter.HasVideoWindow, () => _presenter.ShowAirPlay(),
                                 () => _vm.AlwaysOnTop, onTop => _vm.AlwaysOnTop = onTop);
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
            // Windows is logging off or shutting down: stop uxplay now, the UI thread may not get another turn
            _controller?.ShutdownNow();
            _settings?.Flush();
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _showWait?.Unregister(null);
            _tray?.Dispose();
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
        private void ExitForUpdate(string stagingDir)
        {
            try
            {
                UpdateHelper.Launch(stagingDir);
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is System.IO.IOException ||
                                          error is UnauthorizedAccessException)
            {
                _vm.Update.ReportLaunchFailure(error.Message);
                return;
            }
            ExitApp();
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
            _tray.Dispose();
            _tray = null;
            _bluetooth?.Dispose();
            _presenter?.Dispose();
            await _controller.StopAsync();
            Shutdown();
        }
    }
}

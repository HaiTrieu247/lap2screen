using System;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Native;
using MacExtend.Views;

namespace MacExtend.Services
{
    /// <summary>
    /// While a Mac is connected: brings uxplay's full-screen window to the front when it appears
    /// and hides the Windows pointer while that window is the one in front. As soon as the user
    /// switches away (Alt+Tab, Start, another app) the pointer comes back.
    /// </summary>
    internal sealed class AirPlayPresenter : IDisposable
    {
        private readonly ReceiverController _controller;
        private readonly Func<AppSettings> _settings;
        private readonly DispatcherTimer _scan;
        // kept in a field: the native hook calls it for as long as the hook lives
        private readonly Win32.WinEventProc _onForeground;
        private IntPtr _hook;
        private CursorShield _shield;
        private IntPtr _video;

        public AirPlayPresenter(ReceiverController controller, Func<AppSettings> settings, Dispatcher ui)
        {
            _controller = controller;
            _settings = settings;
            _scan = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, __) => Scan(), ui);
            _scan.Stop();
            _controller.StateChanged += OnStateChanged;
            _onForeground = (hook, eventType, hwnd, idObject, idChild, thread, time) => UpdateShield();
            _hook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                                          _onForeground, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        }

        public bool HasVideoWindow => VideoWindow.IsAlive(_video);

        /// <summary>Back to the Mac display after switching away, e.g. from the tray icon.</summary>
        public void ShowAirPlay()
        {
            if (!HasVideoWindow) return;
            VideoWindow.BringToFront(_video);
            UpdateShield();
        }

        /// <summary>Apply a changed "hide pointer" setting right away.</summary>
        public void SettingsChanged() => UpdateShield();

        public void Dispose()
        {
            _controller.StateChanged -= OnStateChanged;
            _scan.Stop();
            if (_hook != IntPtr.Zero) Win32.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
            _shield?.Close();
            _shield = null;
        }

        private void OnStateChanged()
        {
            if (_controller.Phase == ReceiverPhase.Connected)
            {
                if (!_scan.IsEnabled) _scan.Start();
                return;
            }
            _scan.Stop();
            _video = IntPtr.Zero;
            UpdateShield();
        }

        private void Scan()
        {
            if (!VideoWindow.IsAlive(_video))
            {
                // the window opens once video starts flowing, a moment after the Mac connects
                var found = VideoWindow.Find(_controller.ProcessId);
                if (found != _video)
                {
                    _video = found;
                    if (found != IntPtr.Zero && _settings().FocusOnConnect) VideoWindow.BringToFront(found);
                }
            }
            UpdateShield();
        }

        private void UpdateShield()
        {
            if (_settings().HideCursor && VideoWindow.IsInFront(_video))
            {
                if (_shield == null) _shield = new CursorShield();
                _shield.Cover(VideoWindow.MonitorBounds(_video));
            }
            else
            {
                _shield?.Uncover();
            }
        }
    }
}

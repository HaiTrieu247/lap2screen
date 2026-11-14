using System;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Native;
using MacExtend.Views;

namespace MacExtend.Services
{
    /// <summary>
    /// While a device is connected: brings uxplay's video window to the front when it appears, shrinks it
    /// once to fit the screen if it opens larger than the work area, keeps it above other apps when the user
    /// asked for that, and hides the Windows pointer while that window is full screen and in front. As soon
    /// as the user switches away (Alt+Tab, Start, another app) or leaves full screen, the pointer comes back.
    /// While the laptop controls the device over Bluetooth the pointer stays hidden over the video in a normal
    /// window too. While direct control takes touches on the video, the pointer is never hidden there: the user points.
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

        // the video window being followed; everything below belongs to that one window
        private IntPtr _video;
        private int _videoProcess;
        private bool _fitDone;
        private bool _fitCandidate;
        private PixelRect _fitBounds;
        private bool _pinned;
        private IntPtr _controlled;
        private IntPtr _touch;

        public AirPlayPresenter(ReceiverController controller, Func<AppSettings> settings, Dispatcher ui)
        {
            _controller = controller;
            _settings = settings;
            _scan = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, __) => Scan(), ui);
            _scan.Stop();
            _controller.StateChanged += OnStateChanged;
            _onForeground = (hook, eventType, hwnd, idObject, idChild, thread, time) =>
            {
                UpdateShield();
                ForegroundChanged?.Invoke();
            };
            _hook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                                          _onForeground, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        }

        public bool HasVideoWindow => VideoWindow.IsAlive(_video);

        /// <summary>uxplay's video window while it is open, IntPtr.Zero otherwise.</summary>
        public IntPtr Video => HasVideoWindow ? _video : IntPtr.Zero;

        /// <summary>The pointer shield while it covers the video, IntPtr.Zero otherwise.</summary>
        public IntPtr Shield => _shield != null && _shield.IsCovering ? _shield.Handle : IntPtr.Zero;

        /// <summary>Another video window is followed, or none any more.</summary>
        public event Action VideoChanged;

        /// <summary>Another window came to the front; raised on the UI thread.</summary>
        public event Action ForegroundChanged;

        /// <summary>Hide the pointer over this video window while it is controlled; IntPtr.Zero ends that.</summary>
        public void SetControlled(IntPtr video)
        {
            _controlled = video;
            UpdateShield();
        }

        /// <summary>Direct control takes touches on this video window (no pointer shield over it); IntPtr.Zero ends that.</summary>
        public void SetTouch(IntPtr video)
        {
            if (_touch == video) return;
            _touch = video;
            UpdateShield();
        }

        /// <summary>Back to the AirPlay video after switching away, e.g. from the tray icon.</summary>
        public void ShowAirPlay()
        {
            if (!HasVideoWindow) return;
            VideoWindow.BringToFront(_video);
            ApplyTopmost();
            UpdateShield();
        }

        /// <summary>Apply a changed "hide pointer" or "always on top" setting right away.</summary>
        public void SettingsChanged()
        {
            ApplyTopmost();
            UpdateShield();
        }

        public void Dispose()
        {
            _controller.StateChanged -= OnStateChanged;
            _scan.Stop();
            Follow(IntPtr.Zero, 0);
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
            Follow(IntPtr.Zero, 0);
            UpdateShield();
        }

        private void Scan()
        {
            var process = _controller.ProcessId;
            if (process != _videoProcess || !VideoWindow.IsAlive(_video))
            {
                // the window opens once video starts flowing, a moment after the device connects. After the
                // user closes it, or another device takes over, uxplay opens a new one in the same process.
                var found = VideoWindow.Find(process);
                if (found != _video || process != _videoProcess)
                {
                    Follow(found, process);
                    if (found != IntPtr.Zero && _settings().FocusOnConnect) VideoWindow.BringToFront(found);
                }
            }
            if (_video != IntPtr.Zero && !_fitDone) FitOnce();
            ApplyTopmost();
            UpdateShield();
        }

        private void Follow(IntPtr video, int process)
        {
            // a window left behind, e.g. when the session ends, must not stay pinned over everything
            if (_pinned) VideoWindow.SetTopmost(_video, false);
            _pinned = false;
            var changed = video != _video;
            _video = video;
            _videoProcess = process;
            _fitDone = false;
            _fitCandidate = false;
            if (changed) VideoChanged?.Invoke();
        }

        /// <summary>
        /// d3d11videosink opens its window as large as the picture plus a frame, taller than the screen when
        /// the device sends the laptop's own resolution. Waits until the window has stayed a normal window of
        /// the same size for two scans in a row (full screen, maximized, minimized or being dragged do not
        /// count), then shrinks it to fit or moves it back on screen, once. Later scans leave the user's
        /// size and position alone.
        /// </summary>
        private void FitOnce()
        {
            if (!VideoWindow.TryGetNormalBounds(_video, out var bounds))
            {
                _fitCandidate = false;
                return;
            }
            if (!_fitCandidate || bounds != _fitBounds)
            {
                _fitCandidate = true;
                _fitBounds = bounds;
                return;
            }

            if (!VideoWindow.TryGetWorkArea(_video, out var work)) return;
            var target = WindowFit.Fit(bounds, work);
            if (target == bounds || VideoWindow.MoveIfUnchanged(_video, _videoProcess, bounds, target))
            {
                _fitDone = true;
                return;
            }
            // changed in between or refused: start over with two fresh scans
            _fitCandidate = false;
        }

        /// <summary>
        /// Matches the window's topmost flag to the setting and the window's mode. Runs every scan, so a pin
        /// dropped by BringToFront, by the sink leaving full screen or by anything else comes back within one scan.
        /// </summary>
        private void ApplyTopmost()
        {
            if (!VideoWindow.IsAlive(_video))
            {
                _pinned = false;
                return;
            }
            var topmost = VideoWindow.IsTopmost(_video);
            switch (TopmostPolicy.Decide(_settings().AlwaysOnTop, Win32.IsIconic(_video), VideoWindow.IsFullscreen(_video), topmost, _pinned))
            {
                case TopmostAction.Set:
                    _pinned = VideoWindow.SetTopmost(_video, true);
                    break;
                case TopmostAction.Clear:
                    // refused: still ours, tried again next scan
                    _pinned = !VideoWindow.SetTopmost(_video, false);
                    break;
                default:
                    // a pin dropped by someone else is no longer ours to clear
                    if (!topmost) _pinned = false;
                    break;
            }
        }

        private void UpdateShield()
        {
            if (_touch != IntPtr.Zero && _touch == _video)
            {
                // the direct-control overlay is over the picture and the pointer is how the user aims
                _shield?.Uncover();
                return;
            }
            var controlled = _controlled != IntPtr.Zero && _controlled == _video && VideoWindow.IsAlive(_video);
            if ((controlled || _settings().HideCursor) && VideoWindow.IsInFront(_video) && VideoWindow.IsFullscreen(_video))
            {
                if (_shield == null) _shield = new CursorShield();
                _shield.Cover(VideoWindow.MonitorBounds(_video));
            }
            else if (controlled && VideoWindow.IsInFront(_video) && Win32.GetWindowRect(_video, out var window))
            {
                // only while controlled: over a normal window the shield would swallow clicks on its frame
                if (_shield == null) _shield = new CursorShield();
                _shield.Cover(window);
            }
            else
            {
                _shield?.Uncover();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using MacExtend.Core.Bluetooth;
using MacExtend.Native;

namespace MacExtend.Services.Bluetooth
{
    internal enum StopReason
    {
        /// <summary>Ctrl+Alt+Q.</summary>
        Hotkey,
        /// <summary>Alt+Tab or Alt+Enter: Windows gets the shortcut.</summary>
        WindowsShortcut,
        /// <summary>From the tray menu or the app.</summary>
        Request,
        /// <summary>The video lost the foreground, the session was locked or another desktop came up.</summary>
        Focus,
        /// <summary>The video closed, mirroring ended or the device dropped off Bluetooth.</summary>
        Disconnected,
        Error,
    }

    /// <summary>
    /// Low-level keyboard and mouse hooks on a thread of their own, so a busy UI thread never holds up Windows
    /// input. While watching, the mouse hook only reports left clicks on the AirPlay picture and lets every
    /// event through. While controlling, keys, buttons, wheel and motion go to the <see cref="ReportQueue"/>
    /// instead of Windows, with the pointer parked in the middle of the picture. Key presses are turned into
    /// HID reports and nothing else: no key is kept, logged or reported anywhere.
    /// Methods are called on the UI thread; events are raised on the hook thread.
    /// </summary>
    internal sealed class InputCapture : IDisposable
    {
        private const int WatchMessage = Win32.WM_APP + 1;
        private const int BeginMessage = Win32.WM_APP + 2;
        private const int EndMessage = Win32.WM_APP + 3;
        private const int UpdateHooksMessage = Win32.WM_APP + 4;
        private static readonly IntPtr Swallow = new IntPtr(1);
        /// <summary>Windows waits at most a second before a held key repeats; a later press is a new one.</summary>
        private const uint RepeatWindowMs = 1100;

        private readonly ReportQueue _reports;
        private readonly Action _wake;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _queueReady = new ManualResetEventSlim();
        // kept in fields: the native hooks call them for as long as they are installed
        private readonly Win32.LowLevelProc _keyboardProc;
        private readonly Win32.LowLevelProc _mouseProc;
        private uint _threadId;

        // hook thread only from here on
        private IntPtr _keyboardHook;
        private IntPtr _mouseHook;
        private IntPtr _watched;
        private bool _session;
        private int _sessionId;
        // false as soon as a hotkey ends the session, before the end message is handled
        private bool _controlling;
        private int _centerX;
        private int _centerY;
        private KeyRouter _keys;
        private MouseRouter _mouse;
        private IReadOnlyList<int> _handOver = new int[0];
        private bool _fineTimer;
        // pressed while controlling, with the time of their last event: Windows never saw them go down, so it must
        // not see them repeat or go up either
        private readonly Dictionary<int, uint> _swallowedKeys = new Dictionary<int, uint>();
        private MouseButtons _swallowedButtons;

        public InputCapture(ReportQueue reports, Action wake)
        {
            _reports = reports;
            _wake = wake;
            _keyboardProc = OnKeyboard;
            _mouseProc = OnMouse;
            // MTA: an STA thread would dispatch WinRT completions through the pump the hooks keep busy
            _thread = new Thread(Run) { IsBackground = true, Name = "MacExtend input capture" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
            _queueReady.Wait();
        }

        /// <summary>A left click landed on the watched picture, screen pixels. The click itself went to Windows.</summary>
        public event Action<int, int> VideoClicked;
        /// <summary>
        /// Input is back with Windows, for the session started with that id; also raised when a requested start
        /// could not happen.
        /// </summary>
        public event Action<StopReason, int> Stopped;
        public event Action<string> Log;

        /// <summary>Report left clicks on this window's picture; IntPtr.Zero stops watching.</summary>
        public void Watch(IntPtr video) => Post(WatchMessage, video, IntPtr.Zero);

        /// <param name="sessionId">Comes back with <see cref="Stopped"/>, so a late stop is not taken for a newer session's.</param>
        public void Begin(IntPtr video, int sessionId) => Post(BeginMessage, video, new IntPtr(sessionId));

        public void End(StopReason reason) => Post(EndMessage, IntPtr.Zero, new IntPtr((int)reason));

        public void Dispose()
        {
            Post(Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        private void Post(int message, IntPtr wParam, IntPtr lParam) => Win32.PostThreadMessage(_threadId, message, wParam, lParam);

        private void Run()
        {
            _threadId = Win32.GetCurrentThreadId();
            // the thread gets its message queue on its first message call; PostThreadMessage fails before that
            Win32.PeekMessage(out _, IntPtr.Zero, Win32.WM_APP, Win32.WM_APP, Win32.PM_NOREMOVE);
            _queueReady.Set();

            while (Win32.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                switch ((int)message.message)
                {
                    case WatchMessage:
                        _watched = message.wParam;
                        UpdateHooks();
                        break;
                    case BeginMessage:
                        BeginSession(message.wParam, message.lParam.ToInt32());
                        break;
                    case EndMessage:
                        EndSession((StopReason)message.lParam.ToInt32());
                        break;
                    case UpdateHooksMessage:
                        UpdateHooks();
                        break;
                    default:
                        Win32.TranslateMessage(ref message);
                        Win32.DispatchMessage(ref message);
                        break;
                }
            }

            EndSession(StopReason.Request);
            _watched = IntPtr.Zero;
            _swallowedKeys.Clear();
            _swallowedButtons = MouseButtons.None;
            UpdateHooks();
        }

        private void UpdateHooks()
        {
            SetHook(ref _mouseHook, Win32.WH_MOUSE_LL, _mouseProc, _controlling || _watched != IntPtr.Zero || _swallowedButtons != MouseButtons.None);
            SetHook(ref _keyboardHook, Win32.WH_KEYBOARD_LL, _keyboardProc, _controlling || _swallowedKeys.Count > 0);
        }

        private void SetHook(ref IntPtr hook, int type, Win32.LowLevelProc proc, bool wanted)
        {
            if (wanted == (hook != IntPtr.Zero)) return;
            if (!wanted)
            {
                Win32.UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
                return;
            }
            hook = Win32.SetWindowsHookEx(type, proc, Win32.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) Log?.Invoke("could not install input hook " + type + ": error " + Marshal.GetLastWin32Error());
        }

        // ---- session -----------------------------------------------------------------------

        private void BeginSession(IntPtr video, int sessionId)
        {
            if (_session) return;
            _sessionId = sessionId;
            if (!VideoWindow.TryGetClientBounds(video, out var picture))
            {
                Stopped?.Invoke(StopReason.Disconnected, sessionId);
                return;
            }
            _keys = new KeyRouter(HeldKeys());
            _mouse = new MouseRouter(HeldButtons());
            _centerX = picture.CenterX;
            _centerY = picture.CenterY;
            _session = true;
            _controlling = true;
            UpdateHooks();
            if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            {
                EndSession(StopReason.Error);
                return;
            }
            // pointer reports are paced in milliseconds; the default 15.6 ms timer makes the pointer lag
            _fineTimer = Win32.timeBeginPeriod(1) == 0;
            Win32.SetCursorPos(_centerX, _centerY);
        }

        private void EndSession(StopReason reason)
        {
            if (!_session) return;
            _session = false;
            _controlling = false;
            _reports.EnqueueKeyboard(HidReports.KeyboardRelease());
            if (_mouse.ReleaseAll()) _reports.EnqueueMouse(MouseButtons.None, 0, 0, 0);
            _wake();
            UpdateHooks();
            if (reason == StopReason.WindowsShortcut)
            {
                // the hook is out of the way or ignores injected keys: Windows now sees the whole shortcut
                foreach (var key in _handOver) Win32.keybd_event((byte)key, 0, IsExtended(key) ? Win32.KEYEVENTF_EXTENDEDKEY : 0, UIntPtr.Zero);
            }
            _handOver = new int[0];
            if (_fineTimer) Win32.timeEndPeriod(1);
            _fineTimer = false;
            Stopped?.Invoke(reason, _sessionId);
        }

        /// <summary>Ends the session from inside a hook callback: input stops going to the device at once.</summary>
        private void StopFromHook(StopReason reason)
        {
            _controlling = false;
            Post(EndMessage, IntPtr.Zero, new IntPtr((int)reason));
        }

        private static IEnumerable<int> HeldKeys()
        {
            for (var key = 0x08; key <= 0xFE; key++)
            {
                // the hook reports left/right-specific codes; the generic Shift, Ctrl and Alt would double them
                if (key == 0x10 || key == 0x11 || key == 0x12) continue;
                if ((Win32.GetAsyncKeyState(key) & 0x8000) != 0) yield return key;
            }
        }

        private static MouseButtons HeldButtons()
        {
            var held = MouseButtons.None;
            if ((Win32.GetAsyncKeyState(Win32.VK_LBUTTON) & 0x8000) != 0) held |= MouseButtons.Left;
            if ((Win32.GetAsyncKeyState(Win32.VK_RBUTTON) & 0x8000) != 0) held |= MouseButtons.Right;
            if ((Win32.GetAsyncKeyState(Win32.VK_MBUTTON) & 0x8000) != 0) held |= MouseButtons.Middle;
            return held;
        }

        /// <summary>Right Ctrl, right Alt and the Windows keys carry the E0 prefix.</summary>
        private static bool IsExtended(int key) => key == 0xA3 || key == 0xA5 || key == 0x5B || key == 0x5C;

        // ---- hooks: keep these fast, Windows drops a hook that keeps input waiting ----------------

        private IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code != Win32.HC_ACTION) return Win32.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            var data = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            // our own hand-over keys, and other apps' synthetic input, stay with Windows
            if ((data.flags & Win32.LLKHF_INJECTED) != 0) return Win32.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

            var key = (int)data.vkCode;
            var message = wParam.ToInt32();
            var down = message == Win32.WM_KEYDOWN || message == Win32.WM_SYSKEYDOWN;

            if (!_controlling)
            {
                if (down)
                {
                    // still held from the session: an auto-repeat, which would type into whatever window has focus now
                    if (_swallowedKeys.TryGetValue(key, out var last) && unchecked(data.time - last) < RepeatWindowMs)
                    {
                        _swallowedKeys[key] = data.time;
                        return Swallow;
                    }
                    // a fresh press: the release of the earlier one happened where no hook could see it
                    _swallowedKeys.Remove(key);
                    return Win32.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
                }
                if (!_swallowedKeys.Remove(key)) return Win32.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
                if (_swallowedKeys.Count == 0) Post(UpdateHooksMessage, IntPtr.Zero, IntPtr.Zero);
                return Swallow;
            }

            switch (_keys.OnKey(key, down, data.scanCode, (data.flags & Win32.LLKHF_EXTENDED) != 0))
            {
                case KeyAction.Forward:
                    if (down) _swallowedKeys[key] = data.time;
                    else _swallowedKeys.Remove(key);
                    Send(_keys.Report);
                    return Swallow;
                case KeyAction.Stop:
                    _swallowedKeys[key] = data.time;
                    Send(_keys.Report);
                    StopFromHook(StopReason.Hotkey);
                    return Swallow;
                case KeyAction.HandToWindows:
                    Send(_keys.Report);
                    // Windows gets these pressed again by injection, so it must see them released too
                    _handOver = _keys.HandOver;
                    foreach (var held in _handOver) _swallowedKeys.Remove(held);
                    StopFromHook(StopReason.WindowsShortcut);
                    return Swallow;
                default:
                    // a key Windows owns; or the release of one pressed during an earlier session
                    return !down && _swallowedKeys.Remove(key) ? Swallow : Win32.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            }
        }

        private void Send(byte[] keyboardReport)
        {
            _reports.EnqueueKeyboard(keyboardReport);
            _wake();
        }

        private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code != Win32.HC_ACTION) return Win32.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            var data = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
            if ((data.flags & Win32.LLMHF_INJECTED) != 0) return Win32.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

            var message = wParam.ToInt32();
            var button = ButtonOf(message, out var down);

            if (!_controlling)
            {
                if (button != MouseButtons.None)
                {
                    if (!down && (_swallowedButtons & button) != 0)
                    {
                        _swallowedButtons &= ~button;
                        if (_swallowedButtons == MouseButtons.None) Post(UpdateHooksMessage, IntPtr.Zero, IntPtr.Zero);
                        return Swallow;
                    }
                    if (down) _swallowedButtons &= ~button;
                    if (down && button == MouseButtons.Left && _watched != IntPtr.Zero &&
                        VideoWindow.TryGetClientBounds(_watched, out var picture) && picture.Contains(data.pt.X, data.pt.Y))
                    {
                        VideoClicked?.Invoke(data.pt.X, data.pt.Y);
                    }
                }
                return Win32.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            }

            if (button != MouseButtons.None)
            {
                if (!_mouse.OnButton(button, down))
                {
                    if (!down && (_swallowedButtons & button) != 0)
                    {
                        _swallowedButtons &= ~button;
                        return Swallow;
                    }
                    return Win32.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
                }
                _swallowedButtons = down ? _swallowedButtons | button : _swallowedButtons & ~button;
                _reports.EnqueueMouse(_mouse.Buttons, 0, 0, 0);
                _wake();
                return Swallow;
            }

            if (message == Win32.WM_MOUSEMOVE)
            {
                var dx = data.pt.X - _centerX;
                var dy = data.pt.Y - _centerY;
                if (dx != 0 || dy != 0)
                {
                    _reports.EnqueueMouse(_mouse.Buttons, dx, dy, 0);
                    _wake();
                    Win32.SetCursorPos(_centerX, _centerY);
                }
            }
            else if (message == Win32.WM_MOUSEWHEEL)
            {
                var notches = _mouse.OnWheel((short)((data.mouseData >> 16) & 0xFFFF));
                if (notches != 0)
                {
                    _reports.EnqueueMouse(_mouse.Buttons, 0, 0, notches);
                    _wake();
                }
            }
            // anything else (horizontal wheel, side buttons) has no use on the device, and must not reach Windows
            return Swallow;
        }

        private static MouseButtons ButtonOf(int message, out bool down)
        {
            down = message == Win32.WM_LBUTTONDOWN || message == Win32.WM_RBUTTONDOWN || message == Win32.WM_MBUTTONDOWN;
            switch (message)
            {
                case Win32.WM_LBUTTONDOWN:
                case Win32.WM_LBUTTONUP:
                    return MouseButtons.Left;
                case Win32.WM_RBUTTONDOWN:
                case Win32.WM_RBUTTONUP:
                    return MouseButtons.Right;
                case Win32.WM_MBUTTONDOWN:
                case Win32.WM_MBUTTONUP:
                    return MouseButtons.Middle;
                default:
                    return MouseButtons.None;
            }
        }
    }
}

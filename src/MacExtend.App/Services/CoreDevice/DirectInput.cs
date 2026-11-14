using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Services.CoreDevice
{
    /// <summary>
    /// What the AirPlay picture sends to one serve-web: points as fractions of the upright picture, turned into touch
    /// coordinates by the interface orientation at the moment they are sent; keys as the full set held; Home and volume;
    /// a wheel turn as a short resting drag; text through the device clipboard. A CoreDeviceClient takes no input after a
    /// failed request, so the next call gets a fresh one and the old one lifts what it may still hold. Calls come from the
    /// UI thread; nothing here waits for serve-web there. Nothing typed or pasted is logged.
    /// </summary>
    internal sealed class DirectInput : IDisposable
    {
        private static readonly TimeSpan ReleaseDeadline = TimeSpan.FromSeconds(2);

        private readonly int _port;
        private readonly Func<ScreenOrientation> _orientation;
        private readonly Action<string> _log;
        private readonly TouchStream _touch;
        private readonly object _lock = new object();
        private CoreDeviceClient _client;
        private bool _disposed;
        // the wheel: one drag at a time, the turns meanwhile add up
        private Task _scroll = Task.CompletedTask;
        private double _pendingNotches;
        private bool _pendingHorizontal;
        private double _pendingX;
        private double _pendingY;
        private bool _clipboardBusy;

        /// <param name="orientation">How to turn a point: TouchMap.Effective of the last answer and the picture's shape.</param>
        public DirectInput(int port, Func<ScreenOrientation> orientation, Action<string> log)
        {
            _port = port;
            _orientation = orientation ?? throw new ArgumentNullException(nameof(orientation));
            _log = log ?? (_ => { });
            _client = new CoreDeviceClient(port);
            _touch = new TouchStream(SendTouchAsync, e => _log("touch failed: " + e.GetType().Name));
        }

        public int Port => _port;

        public bool IsPressing => _touch.IsDown;

        public void Down(double xf, double yf)
        {
            Hid(xf, yf, out var x, out var y);
            _touch.Down(x, y);
        }

        public void Move(double xf, double yf)
        {
            Hid(xf, yf, out var x, out var y);
            _touch.Move(x, y);
        }

        public void Up(double xf, double yf)
        {
            Hid(xf, yf, out var x, out var y);
            _touch.Up(x, y);
        }

        /// <summary>The press ends without its gesture: the finger lifts where it was.</summary>
        public void Cancel() => _touch.Cancel();

        public void Press(DeviceButton button) => Observe(() => Client().PressAsync(button, CancellationToken.None), "button");

        /// <summary>The keys held now, as HID usages; an empty set lets every key go.</summary>
        public void Keys(IEnumerable<int> usages)
        {
            var set = usages.Distinct().ToList();
            Observe(() => Client().SetKeysAsync(set, CancellationToken.None), "keys");
        }

        /// <summary>
        /// Ctrl+V: the laptop's text goes to the device clipboard first, then Command+V pastes it where the device's
        /// cursor is. One paste at a time; the keys it presses are its own, so a key-up meanwhile cannot leave V down.
        /// </summary>
        /// <param name="held">The other keys held now (Command among them), which stay down after the paste.</param>
        public void Paste(string text, IReadOnlyCollection<int> held)
        {
            lock (_lock)
            {
                if (_clipboardBusy || _disposed) return;
                _clipboardBusy = true;
            }
            _ = PasteAsync(text, held.ToList());
        }

        public void Scroll(double xf, double yf, double notches, bool horizontal)
        {
            lock (_lock)
            {
                if (_disposed) return;
                if (_pendingNotches != 0 && horizontal != _pendingHorizontal) _pendingNotches = 0;
                _pendingNotches += notches;
                _pendingHorizontal = horizontal;
                _pendingX = xf;
                _pendingY = yf;
                if (!_scroll.IsCompleted) return;
                _scroll = Task.Run(ScrollAsync);
            }
        }

        /// <summary>Lifts the finger and lets every key go, within 2 s, whatever came before.</summary>
        public Task ReleaseAllAsync()
        {
            _touch.Cancel();
            CoreDeviceClient client;
            lock (_lock) client = _client;
            return client.ReleaseAllAsync(ReleaseDeadline);
        }

        public void Dispose()
        {
            CoreDeviceClient client;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                client = _client;
            }
            _touch.Cancel();
            // the release goes after whatever is queued; the client is let go of once it is done
            _ = client.ReleaseAllAsync(ReleaseDeadline).ContinueWith(_ => client.Dispose(), TaskScheduler.Default);
        }

        private void Hid(double xf, double yf, out int x, out int y) => TouchMap.ToHid(xf, yf, _orientation(), out x, out y);

        private Task SendTouchAsync(TouchKind kind, int x, int y)
        {
            var client = Client();
            return kind == TouchKind.Release ? client.ReleaseContactAsync(x, y, CancellationToken.None) : client.ContactAsync(x, y, CancellationToken.None);
        }

        /// <summary>The client to send with: a fresh one after a failure closed the last.</summary>
        private CoreDeviceClient Client()
        {
            CoreDeviceClient old;
            lock (_lock)
            {
                if (_client.IsOpen || _disposed) return _client;
                old = _client;
                _client = new CoreDeviceClient(_port);
            }
            _log("a request failed; input goes on with a new connection");
            _ = old.ReleaseAllAsync(ReleaseDeadline).ContinueWith(_ => old.Dispose(), TaskScheduler.Default);
            lock (_lock) return _client;
        }

        private async Task ScrollAsync()
        {
            while (true)
            {
                double notches, xf, yf;
                bool horizontal;
                lock (_lock)
                {
                    if (_pendingNotches == 0 || _disposed)
                    {
                        _pendingNotches = 0;
                        return;
                    }
                    notches = _pendingNotches;
                    horizontal = _pendingHorizontal;
                    xf = _pendingX;
                    yf = _pendingY;
                    _pendingNotches = 0;
                }
                var drag = ScrollDrag.For(xf, yf, notches, horizontal);
                Hid(drag.X1, drag.Y1, out var x1, out var y1);
                Hid(drag.X2, drag.Y2, out var x2, out var y2);
                try
                {
                    var result = await CoreDeviceGestures.DragAsync(Client(), x1, y1, x2, y2, ScrollDrag.Duration, ScrollDrag.Rest, ReleaseDeadline, CancellationToken.None)
                                                         .ConfigureAwait(false);
                    if (result.End != GestureEnd.Completed) _log("scroll cut at its deadline");
                }
                catch (Exception e) when (e is CoreDeviceClientException || e is OperationCanceledException || e is ArgumentException)
                {
                    _log("scroll failed: " + e.GetType().Name);
                }
            }
        }

        private async Task PasteAsync(string text, List<int> held)
        {
            try
            {
                if (!string.IsNullOrEmpty(text))
                {
                    using (var control = new ServeWebControl(_port, ServeWebControl.ClipboardTimeout))
                    {
                        await control.SendTextAsync(text, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                // Command+V, then the keys as they were
                const int command = 0xE3, v = 0x19;
                var withV = held.Where(u => u != v).ToList();
                if (!withV.Contains(command) && !withV.Contains(0xE7)) withV.Add(command);
                withV.Add(v);
                var client = Client();
                await client.SetKeysAsync(withV, CancellationToken.None).ConfigureAwait(false);
                await client.SetKeysAsync(held.Where(u => u != v), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is CoreDeviceClientException || e is OperationCanceledException || e is ArgumentException)
            {
                _log("paste failed: " + e.GetType().Name);
            }
            finally
            {
                lock (_lock) _clipboardBusy = false;
            }
        }

        /// <summary>A request whose failure is logged by type and never reaches the crash log; the next call gets a fresh client.</summary>
        private void Observe(Func<Task> call, string what)
        {
            Task task;
            try
            {
                task = call();
            }
            catch (ArgumentException e)
            {
                _log(what + " refused: " + e.GetType().Name);
                return;
            }
            task.ContinueWith(t =>
            {
                var error = t.Exception?.GetBaseException();
                _log(what + " failed: " + (error?.GetType().Name ?? "?"));
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MacExtend.Core.Wda
{
    /// <summary>
    /// Runs the overlay's gestures against WDA, one request at a time, on a task of its own: the UI thread only queues.
    /// Positions come as fractions of the picture and become points with the latest screen size, read again whenever
    /// the queue is idle for a second (it swaps when the device rotates). Typing and wheel steps that pile up are sent
    /// together; drags and scrolls are dropped when the queue falls behind, taps and text never.
    /// A request WDA answers with an error is logged and skipped (typing with no field focused, say); one it does not
    /// answer at all ends the driver with <see cref="WdaFailure.Lost"/> unless /status still answers.
    /// </summary>
    public sealed class WdaDriver : IDisposable
    {
        public static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(1);
        /// <summary>A gesture after this long without a size check reads the size first.</summary>
        public static readonly TimeSpan SizeMaxAge = TimeSpan.FromMilliseconds(1500);
        public const int Backlog = 8;
        public const int MaxClipboardText = 2000;
        /// <summary>Long side over short side below this is an iPhone with a Home button (16:9), above an iPad's 4:3 to 10:7.</summary>
        public const double HomeButtonMaxElongation = 2.0;
        public const double HomeButtonMinElongation = 1.6;

        private readonly WdaClient _client;
        private readonly Func<DateTime> _clock;
        private readonly object _lock = new object();
        private readonly LinkedList<Command> _queue = new LinkedList<Command>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly WdaScroll _scroll = new WdaScroll();
        private DeviceSize _size;
        private DateTime _sizeAt;
        private Task _loop;
        private int _lost;

        public WdaDriver(WdaClient client, Func<DateTime> clock)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>Raised once, on the driver's task, when WDA stopped answering.</summary>
        public event Action<WdaFailure> Lost;

        /// <summary>Raised on the driver's task when the screen size (the orientation) changed.</summary>
        public event Action SizeChanged;

        /// <summary>Short lines for the log: kinds and status codes, never text or positions.</summary>
        public event Action<string> Logged;

        /// <summary>The screen in points, current orientation; unknown before <see cref="StartAsync"/>.</summary>
        public DeviceSize Size
        {
            get { lock (_lock) return _size; }
        }

        /// <summary>A session and the screen size; then the queue runs. Throws <see cref="WdaException"/> when WDA refuses.</summary>
        public async Task StartAsync(CancellationToken token)
        {
            await _client.StartSessionAsync(token).ConfigureAwait(false);
            var size = await _client.WindowSizeAsync(token).ConfigureAwait(false);
            lock (_lock)
            {
                _size = size;
                _sizeAt = _clock();
            }
            _loop = Task.Run(() => LoopAsync());
        }

        public void Tap(double xf, double yf) => Put(new Command(Kind.Tap) { X = xf, Y = yf });

        public void LongPress(double xf, double yf) => Put(new Command(Kind.LongPress) { X = xf, Y = yf });

        /// <param name="samples">The pointer's path in fractions of the picture; the first is where the button went down.</param>
        /// <param name="releaseMs">When the button came up, in the samples' time.</param>
        public void Drag(IReadOnlyList<PathSample> samples, int releaseMs)
        {
            if (samples == null || samples.Count < 2) return;
            Put(new Command(Kind.Drag) { Samples = samples.ToList(), ReleaseMs = releaseMs });
        }

        /// <param name="notches">Wheel notches (fractions for a touchpad); positive is away from the user, or tilted right.</param>
        public void Scroll(double xf, double yf, double notches, bool horizontal)
        {
            if (notches == 0 || double.IsNaN(notches) || double.IsInfinity(notches)) return;
            Put(new Command(Kind.Scroll) { X = xf, Y = yf, Amount = notches, Horizontal = horizontal });
        }

        /// <summary>"\b" deletes, "\n" is Return; a CR or CRLF counts as Return too.</summary>
        public void Type(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            Put(new Command(Kind.Type) { Text = text });
        }

        /// <summary>Text from the clipboard, typed as is: at most <see cref="MaxClipboardText"/> characters.</summary>
        public void Paste(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Type(text.Length > MaxClipboardText ? text.Substring(0, MaxClipboardText) : text);
        }

        public void Home() => Put(new Command(Kind.Home));

        public void AppSwitcher() => Put(new Command(Kind.Switcher));

        public void Volume(bool up) => Put(new Command(Kind.Button) { Text = up ? "volumeUp" : "volumeDown" });

        /// <summary>Stops the queue; a request on its way finishes or is cut by its deadline. Any thread.</summary>
        public void Dispose()
        {
            _stop.Cancel();
            _signal.Release();
        }

        /// <summary>Waits up to <paramref name="timeout"/> for the queue's task to end after <see cref="Dispose"/>.</summary>
        public bool WaitStopped(TimeSpan timeout)
        {
            var loop = _loop;
            return loop == null || loop.Wait(timeout);
        }

        private void Put(Command command)
        {
            if (_stop.IsCancellationRequested || Volatile.Read(ref _lost) != 0) return;
            lock (_lock)
            {
                if (_queue.Count >= Backlog && (command.Kind == Kind.Drag || command.Kind == Kind.Scroll))
                {
                    Write("queue full, " + command.Kind + " dropped");
                    return;
                }
                _queue.AddLast(command);
            }
            _signal.Release();
        }

        private async Task LoopAsync()
        {
            var token = _stop.Token;
            while (!token.IsCancellationRequested)
            {
                bool signalled;
                try
                {
                    signalled = await _signal.WaitAsync(IdleCheck, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                if (token.IsCancellationRequested) return;
                var command = signalled ? Take() : null;
                try
                {
                    if (command == null)
                    {
                        if (!signalled) await RefreshSizeAsync(token).ConfigureAwait(false);
                        continue;
                    }
                    if (_clock() - SizeReadAt() > SizeMaxAge) await RefreshSizeAsync(token).ConfigureAwait(false);
                    await RunAsync(command, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (WdaException e) when (!e.NoAnswer)
                {
                    Write((command?.Kind.ToString() ?? "size check") + " refused: HTTP " + e.Status + (e.Error != null ? " " + e.Error : ""));
                }
                catch (WdaException e)
                {
                    Write((command?.Kind.ToString() ?? "size check") + " got no answer: " + e.Message);
                    if (token.IsCancellationRequested) return;
                    if (await _client.IsReadyAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false)) continue;
                    if (Interlocked.Exchange(ref _lost, 1) == 0) Raise(() => Lost?.Invoke(WdaFailure.Lost));
                    return;
                }
            }
        }

        /// <summary>The next command, with the typing or wheel steps queued right behind it folded in.</summary>
        private Command Take()
        {
            lock (_lock)
            {
                if (_queue.Count == 0) return null;
                var first = _queue.First.Value;
                _queue.RemoveFirst();
                while (_queue.Count > 0 && Folds(first, _queue.First.Value))
                {
                    var next = _queue.First.Value;
                    _queue.RemoveFirst();
                    // the semaphore counted that one too
                    _signal.Wait(0);
                    if (first.Kind == Kind.Type) first.Text += next.Text;
                    else first.Amount += next.Amount;
                }
                return first;
            }
        }

        private static bool Folds(Command a, Command b)
        {
            if (a.Kind != b.Kind) return false;
            if (a.Kind == Kind.Type) return true;
            return a.Kind == Kind.Scroll && a.Horizontal == b.Horizontal && Math.Abs(a.X - b.X) < 0.05 && Math.Abs(a.Y - b.Y) < 0.05;
        }

        private async Task RunAsync(Command c, CancellationToken token)
        {
            var size = Size;
            var w = size.Width;
            var h = size.Height;
            switch (c.Kind)
            {
                case Kind.Tap:
                    await _client.TouchAsync(WdaGestures.Tap(WdaGestures.X(c.X, w), WdaGestures.Y(c.Y, h)), token).ConfigureAwait(false);
                    break;
                case Kind.LongPress:
                    await _client.TouchAsync(WdaGestures.LongPress(WdaGestures.X(c.X, w), WdaGestures.Y(c.Y, h)), token).ConfigureAwait(false);
                    break;
                case Kind.Drag:
                    await _client.TouchAsync(WdaGestures.Path(c.Samples, c.ReleaseMs, w, h), token).ConfigureAwait(false);
                    break;
                case Kind.Scroll:
                    foreach (var drag in _scroll.Drags(c.X, c.Y, c.Amount, c.Horizontal, w, h, _clock()))
                    {
                        await _client.TouchAsync(drag, token).ConfigureAwait(false);
                    }
                    break;
                case Kind.Type:
                    await _client.TypeAsync(c.Text, token).ConfigureAwait(false);
                    break;
                case Kind.Home:
                    await _client.HomeAsync(token).ConfigureAwait(false);
                    break;
                case Kind.Button:
                    await _client.PressButtonAsync(c.Text, token).ConfigureAwait(false);
                    break;
                case Kind.Switcher:
                    await SwitcherAsync(size, token).ConfigureAwait(false);
                    break;
            }
        }

        /// <summary>
        /// Home-button iPhones (16:9) open it with a double press of Home; iPhones with Face ID and every iPad with a drag up
        /// from the bottom edge that stops and holds.
        /// </summary>
        private async Task SwitcherAsync(DeviceSize size, CancellationToken token)
        {
            var e = size.Elongation;
            if (e >= HomeButtonMinElongation && e < HomeButtonMaxElongation)
            {
                try
                {
                    await _client.HidHomeAsync(token).ConfigureAwait(false);
                    await _client.HidHomeAsync(token).ConfigureAwait(false);
                }
                catch (WdaException ex) when (!ex.NoAnswer)
                {
                    // an older build without performIoHidEvent: the slower pressButton
                    await _client.PressButtonAsync("home", token).ConfigureAwait(false);
                    await _client.PressButtonAsync("home", token).ConfigureAwait(false);
                }
                return;
            }
            var w = size.Width;
            var h = size.Height;
            await _client.TouchAsync(WdaGestures.Swipe(w / 2, h - 2, w / 2, h * 0.62, 280, 450), token).ConfigureAwait(false);
        }

        private async Task RefreshSizeAsync(CancellationToken token)
        {
            var size = await _client.WindowSizeAsync(token).ConfigureAwait(false);
            bool changed;
            lock (_lock)
            {
                changed = !size.Equals(_size);
                _size = size;
                _sizeAt = _clock();
            }
            if (changed)
            {
                Write("screen size now " + size);
                Raise(() => SizeChanged?.Invoke());
            }
        }

        private DateTime SizeReadAt()
        {
            lock (_lock) return _sizeAt;
        }

        private void Write(string line) => Raise(() => Logged?.Invoke(line));

        private static void Raise(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // a listener never breaks the queue
            }
        }

        private enum Kind
        {
            Tap,
            LongPress,
            Drag,
            Scroll,
            Type,
            Home,
            Switcher,
            Button,
        }

        private sealed class Command
        {
            public Command(Kind kind)
            {
                Kind = kind;
            }

            public Kind Kind { get; }
            public double X;
            public double Y;
            public double Amount;
            public bool Horizontal;
            public string Text;
            public List<PathSample> Samples;
            public int ReleaseMs;
        }
    }
}

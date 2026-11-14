using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MacExtend.Core.CoreDevice
{
    public enum TouchKind
    {
        /// <summary>The finger down at a point, or moved there.</summary>
        Contact,
        Release,
    }

    /// <summary>
    /// The pointer's finger on the device, live, the way serve-web's own viewer sends it: a press is a contact at once,
    /// moves are further contacts, the release comes last. One touch is in flight at a time. A finger going down or up
    /// is never dropped; moves that pile up behind a slow answer collapse into the latest one, and a move goes at most
    /// once per <see cref="Frame"/>, except right before a release, which waits for nothing. The calls may come from any
    /// thread (the UI's); the touches are sent from the thread pool, and a failed one is reported and not sent again.
    /// </summary>
    public sealed class TouchStream
    {
        public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

        private readonly Func<TouchKind, int, int, Task> _send;
        private readonly Func<TimeSpan> _clock;
        private readonly Func<TimeSpan, Task> _delay;
        private readonly Action<Exception> _failed;
        private readonly object _lock = new object();
        private readonly LinkedList<Touch> _queue = new LinkedList<Touch>();
        private bool _down;
        private int _lastX;
        private int _lastY;
        private bool _pumping;
        // the pump's own: when the last move of this press went out
        private TimeSpan? _lastMoveAt;
        private volatile Task _awaiting;

        /// <param name="send">Sends one touch in HID coordinates; its task ends when serve-web has answered.</param>
        /// <param name="failed">A touch failed (on a pool thread); later ones are still sent.</param>
        public TouchStream(Func<TouchKind, int, int, Task> send, Action<Exception> failed)
            : this(send, MonotonicClock(), span => Task.Delay(span), failed)
        {
        }

        public TouchStream(Func<TouchKind, int, int, Task> send, Func<TimeSpan> clock, Func<TimeSpan, Task> delay, Action<Exception> failed)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _delay = delay ?? throw new ArgumentNullException(nameof(delay));
            _failed = failed;
        }

        /// <summary>The user's finger is down (the release may still be on its way).</summary>
        public bool IsDown
        {
            get { lock (_lock) return _down; }
        }

        /// <summary>Nothing to do until a touch is answered, a frame passes, or the user does something: for tests.</summary>
        public bool IsWaiting
        {
            get
            {
                lock (_lock)
                {
                    if (!_pumping) return true;
                }
                var awaited = _awaiting;
                return awaited != null && !awaited.IsCompleted;
            }
        }

        public void Down(int x, int y)
        {
            lock (_lock)
            {
                if (_down)
                {
                    MoveLocked(x, y);
                    return;
                }
                _down = true;
                _lastX = x;
                _lastY = y;
                _queue.AddLast(new Touch(TouchKind.Contact, x, y, false));
                StartLocked();
            }
        }

        /// <summary>The finger moved; ignored while it is not down.</summary>
        public void Move(int x, int y)
        {
            lock (_lock)
            {
                if (_down) MoveLocked(x, y);
            }
        }

        /// <summary>The finger lifts at (x, y); ignored while it is not down.</summary>
        public void Up(int x, int y)
        {
            lock (_lock)
            {
                if (!_down) return;
                _down = false;
                _lastX = x;
                _lastY = y;
                _queue.AddLast(new Touch(TouchKind.Release, x, y, false));
                StartLocked();
            }
        }

        /// <summary>The press ends without its gesture (Alt+Tab, the picture gone): moves not sent yet are dropped, the finger lifts at its last point.</summary>
        public void Cancel()
        {
            lock (_lock)
            {
                if (!_down) return;
                _down = false;
                for (var node = _queue.First; node != null;)
                {
                    var next = node.Next;
                    if (node.Value.IsMove) _queue.Remove(node);
                    node = next;
                }
                _queue.AddLast(new Touch(TouchKind.Release, _lastX, _lastY, false));
                StartLocked();
            }
        }

        private void MoveLocked(int x, int y)
        {
            _lastX = x;
            _lastY = y;
            var last = _queue.Last;
            var move = new Touch(TouchKind.Contact, x, y, true);
            if (last != null && last.Value.IsMove) last.Value = move;
            else _queue.AddLast(move);
            StartLocked();
        }

        private void StartLocked()
        {
            if (_pumping) return;
            _pumping = true;
            _awaiting = null;
            Task.Run(PumpAsync);
        }

        private async Task PumpAsync()
        {
            while (true)
            {
                Touch next;
                bool alone;
                lock (_lock)
                {
                    if (_queue.Count == 0)
                    {
                        _pumping = false;
                        return;
                    }
                    next = _queue.First.Value;
                    alone = _queue.Count == 1;
                }
                if (next.IsMove && alone && _lastMoveAt != null)
                {
                    var wait = _lastMoveAt.Value + Frame - _clock();
                    if (wait > TimeSpan.Zero)
                    {
                        await Await(_delay(wait)).ConfigureAwait(false);
                        // a newer move or the release may have come meanwhile
                        continue;
                    }
                }
                lock (_lock)
                {
                    next = _queue.First.Value;
                    _queue.RemoveFirst();
                }
                // a press or a release starts the next moves afresh
                _lastMoveAt = next.IsMove ? _clock() : (TimeSpan?)null;
                Task sent;
                try
                {
                    sent = _send(next.Kind, next.X, next.Y);
                }
                catch (Exception e)
                {
                    sent = Task.FromException(e);
                }
                try
                {
                    await Await(sent).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _failed?.Invoke(e);
                }
            }
        }

        private Task Await(Task task)
        {
            _awaiting = task;
            return task;
        }

        private static Func<TimeSpan> MonotonicClock()
        {
            var watch = Stopwatch.StartNew();
            return () => watch.Elapsed;
        }

        private struct Touch
        {
            public Touch(TouchKind kind, int x, int y, bool isMove)
            {
                Kind = kind;
                X = x;
                Y = y;
                IsMove = isMove;
            }

            public TouchKind Kind { get; }
            public int X { get; }
            public int Y { get; }
            /// <summary>A contact after the press, which a newer one may replace.</summary>
            public bool IsMove { get; }
        }
    }
}

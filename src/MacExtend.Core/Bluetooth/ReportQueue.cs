using System.Collections.Generic;

namespace MacExtend.Core.Bluetooth
{
    public struct QueuedReport
    {
        public bool IsKeyboard;
        public byte[] Keyboard;
        public MouseButtons Buttons;
        public int Dx;
        public int Dy;
        public int Wheel;
    }

    /// <summary>
    /// Reports waiting for the Bluetooth link. The hooks produce motion far faster than a connection interval
    /// can carry, so pointer motion is merged; keyboard reports and every button change are kept, in order.
    /// Idea and pacing rationale from windows-ble-hid (MIT), CaptureSession.ReportQueue.
    /// </summary>
    public sealed class ReportQueue
    {
        private readonly object _lock = new object();
        private readonly Queue<QueuedReport> _ordered = new Queue<QueuedReport>();
        private QueuedReport? _mouse;

        public bool HasMouse
        {
            get
            {
                lock (_lock) return _mouse.HasValue;
            }
        }

        public void EnqueueKeyboard(byte[] report)
        {
            lock (_lock)
            {
                // motion before the key stays before it
                FlushMouse();
                _ordered.Enqueue(new QueuedReport { IsKeyboard = true, Keyboard = report });
            }
        }

        public void EnqueueMouse(MouseButtons buttons, int dx, int dy, int wheel)
        {
            lock (_lock)
            {
                // a button change ends the merge, or a quick click would vanish into one report
                if (_mouse.HasValue && _mouse.Value.Buttons != buttons) FlushMouse();
                var pending = _mouse.GetValueOrDefault();
                _mouse = new QueuedReport
                {
                    Buttons = buttons,
                    Dx = pending.Dx + dx,
                    Dy = pending.Dy + dy,
                    Wheel = pending.Wheel + wheel,
                };
            }
        }

        /// <summary>Keyboard reports and button changes: send each one as soon as possible.</summary>
        public bool TryDequeueOrdered(out QueuedReport report)
        {
            lock (_lock)
            {
                if (_ordered.Count > 0)
                {
                    report = _ordered.Dequeue();
                    return true;
                }
                report = default(QueuedReport);
                return false;
            }
        }

        /// <summary>The merged motion, once every ordered report has gone; the caller paces these.</summary>
        public bool TryTakeMouse(out QueuedReport report)
        {
            lock (_lock)
            {
                report = default(QueuedReport);
                if (_ordered.Count > 0 || !_mouse.HasValue) return false;
                report = _mouse.Value;
                _mouse = null;
                return true;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _ordered.Clear();
                _mouse = null;
            }
        }

        private void FlushMouse()
        {
            if (!_mouse.HasValue) return;
            _ordered.Enqueue(_mouse.Value);
            _mouse = null;
        }
    }
}

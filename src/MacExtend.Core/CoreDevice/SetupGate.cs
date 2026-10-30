using System;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// One setup action at a time in the whole app, whichever setup window started it. A window closed during the
    /// elevated setup, a mount or a pairing lets that action finish in the background; a window opened meanwhile must
    /// not start a second one (a second UAC, two helpers staging the runtime) and shows the running step and its message
    /// instead. Only the owner changes the step, the message or lets go; every change raises <see cref="Changed"/>,
    /// outside the lock.
    /// </summary>
    public sealed class SetupGate
    {
        private readonly object _lock = new object();
        private object _owner;
        private SetupStepId? _step;
        private string _message = "";

        public event Action Changed;

        public object Owner { get { lock (_lock) return _owner; } }
        public bool IsHeld { get { lock (_lock) return _owner != null; } }
        /// <summary>The step whose action runs now ("Đang làm"); null for a plain check.</summary>
        public SetupStepId? Step { get { lock (_lock) return _step; } }
        public string Message { get { lock (_lock) return _message; } }

        /// <returns>false when an action already runs (this owner's or another's).</returns>
        public bool TryEnter(object owner, SetupStepId? step, string message)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            lock (_lock)
            {
                if (_owner != null) return false;
                _owner = owner;
                _step = step;
                _message = message ?? "";
            }
            Changed?.Invoke();
            return true;
        }

        public void SetStep(object owner, SetupStepId? step)
        {
            lock (_lock)
            {
                if (_owner == null || !ReferenceEquals(_owner, owner)) return;
                _step = step;
            }
            Changed?.Invoke();
        }

        public void SetMessage(object owner, string message)
        {
            lock (_lock)
            {
                if (_owner == null || !ReferenceEquals(_owner, owner)) return;
                _message = message ?? "";
            }
            Changed?.Invoke();
        }

        public void Exit(object owner)
        {
            lock (_lock)
            {
                if (_owner == null || !ReferenceEquals(_owner, owner)) return;
                _owner = null;
                _step = null;
                _message = "";
            }
            Changed?.Invoke();
        }
    }
}

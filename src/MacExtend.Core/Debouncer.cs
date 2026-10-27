using System;

namespace MacExtend.Core
{
    /// <summary>Coalesces bursts of signals (for example Wi-Fi reconnecting) into one action after a quiet period.</summary>
    public sealed class Debouncer
    {
        private readonly TimeSpan _quiet;
        private readonly Func<DateTime> _clock;
        private DateTime? _due;

        public Debouncer(TimeSpan quiet, Func<DateTime> clock)
        {
            _quiet = quiet;
            _clock = clock;
        }

        public void Signal() => _due = _clock() + _quiet;

        /// <summary>Poll this; it returns true exactly once per burst.</summary>
        public bool ShouldFire()
        {
            if (_due == null || _clock() < _due.Value) return false;
            _due = null;
            return true;
        }
    }
}

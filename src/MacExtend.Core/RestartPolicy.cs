using System;
using System.Collections.Generic;

namespace MacExtend.Core
{
    /// <summary>Back-off for restarting a crashed uxplay; gives up when it keeps failing.</summary>
    public sealed class RestartPolicy
    {
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan[] Delays =
        {
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
        };

        private readonly Func<DateTime> _clock;
        private readonly Queue<DateTime> _failures = new Queue<DateTime>();

        public RestartPolicy(Func<DateTime> clock)
        {
            _clock = clock;
        }

        /// <summary>Records a failure.</summary>
        /// <returns>How long to wait before restarting, or null to give up.</returns>
        public TimeSpan? NextDelay()
        {
            var now = _clock();
            while (_failures.Count > 0 && now - _failures.Peek() > Window) _failures.Dequeue();
            _failures.Enqueue(now);
            return _failures.Count > Delays.Length ? (TimeSpan?)null : Delays[_failures.Count - 1];
        }

        /// <summary>Call once the process has been running long enough to count as healthy.</summary>
        public void ReportHealthy() => _failures.Clear();
    }
}

using System;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>A device and the route to open it over.</summary>
    public sealed class SessionRequest
    {
        public SessionRequest(string udid, CoreDeviceRoute route)
        {
            if (!CoreDeviceOptions.IsValidUdid(udid)) throw new ArgumentException("a session needs a valid device UDID", nameof(udid));
            if (route != CoreDeviceRoute.Usb && route != CoreDeviceRoute.Wifi) throw new ArgumentException("a session needs a route", nameof(route));
            Udid = udid;
            Route = route;
        }

        public string Udid { get; }

        public CoreDeviceRoute Route { get; }
    }

    public enum OpenOutcome
    {
        /// <summary>A new session, with a new generation, starts now.</summary>
        Started,

        /// <summary>The previous session is still stopping: this request starts once its teardown is over.</summary>
        Queued,

        /// <summary>A session is running: the request is ignored.</summary>
        AlreadyRunning,
    }

    /// <summary>
    /// One direct-control session at a time. Each session gets a generation; a session holds the slot from its start
    /// until its teardown (serve-web and the tunnel stopped) is over, and anything that reports a generation other than
    /// the one holding the slot is stale and must be ignored. An open asked for while the holder is stopping waits, and
    /// the latest such request wins. Not thread-safe: the service calls it under its own lock.
    /// </summary>
    public sealed class SessionSlot
    {
        /// <summary>The generation of the latest session; 0 before the first.</summary>
        public int Generation { get; private set; }

        /// <summary>The latest session has not been torn down yet.</summary>
        public bool Occupied { get; private set; }

        /// <summary>The request waiting for the current session's teardown, or null.</summary>
        public SessionRequest Queued { get; private set; }

        /// <param name="currentIsOver">The session holding the slot is Failed, Stopping or Stopped (SessionMachine.IsOver).</param>
        public OpenOutcome Open(SessionRequest request, bool currentIsOver)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!Occupied)
            {
                Generation++;
                Occupied = true;
                Queued = null;
                return OpenOutcome.Started;
            }
            if (!currentIsOver) return OpenOutcome.AlreadyRunning;
            Queued = request;
            return OpenOutcome.Queued;
        }

        public bool IsCurrent(int generation) => Occupied && generation == Generation;

        /// <summary>A stop: the waiting request, if any, is dropped.</summary>
        public void CancelQueued() => Queued = null;

        /// <summary>
        /// The session of <paramref name="generation"/> is torn down. Returns the waiting request, which now holds the
        /// slot as the next generation, or null (the slot is free, or the generation was stale).
        /// </summary>
        public SessionRequest TornDown(int generation)
        {
            if (!IsCurrent(generation)) return null;
            var next = Queued;
            Queued = null;
            if (next == null)
            {
                Occupied = false;
                return null;
            }
            Generation++;
            return next;
        }
    }
}

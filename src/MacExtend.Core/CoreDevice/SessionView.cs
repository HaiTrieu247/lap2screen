using System;
using System.Globalization;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// What the window, the tray and the settings page see of the direct-control session: one immutable, consistent
    /// snapshot, so a reader never mixes the phase of one moment with the port of another.
    /// </summary>
    public sealed class SessionView : IEquatable<SessionView>
    {
        public static readonly SessionView Idle = new SessionView(SessionPhase.Idle, SessionFailure.None, CoreDeviceRoute.None, null, 0, false, false);

        private SessionView(SessionPhase phase, SessionFailure failure, CoreDeviceRoute route, string udid, int port, bool lastStopWasForced, bool waitingForPreviousStop)
        {
            Phase = phase;
            Failure = failure;
            Route = route;
            Udid = udid;
            Port = port;
            LastStopWasForced = lastStopWasForced;
            WaitingForPreviousStop = waitingForPreviousStop;
        }

        /// <summary>
        /// A session as its machine and serve-web stand: <paramref name="servingPort"/> counts only while the viewer can be
        /// used (Ready, Reconnecting) and the failure only once the session failed.
        /// </summary>
        public static SessionView Of(SessionPhase phase, SessionFailure failure, CoreDeviceRoute route, string udid, int servingPort, bool lastStopWasForced)
        {
            var viewer = phase == SessionPhase.Ready || phase == SessionPhase.Reconnecting;
            return new SessionView(phase, phase == SessionPhase.Failed ? failure : SessionFailure.None, route, udid,
                                   viewer && servingPort > 0 ? servingPort : 0, lastStopWasForced, false);
        }

        /// <summary>A new session asked for while the previous one is still stopping; it starts once that stop is over.</summary>
        public static SessionView WaitingFor(CoreDeviceRoute route, string udid) =>
            new SessionView(SessionPhase.Stopping, SessionFailure.None, route, udid, 0, false, true);

        public SessionPhase Phase { get; }

        /// <summary>None unless <see cref="Phase"/> is Failed.</summary>
        public SessionFailure Failure { get; }

        public CoreDeviceRoute Route { get; }

        public string Udid { get; }

        /// <summary>serve-web's loopback port while its viewer can be used; 0 otherwise.</summary>
        public int Port { get; }

        public string ViewerUrl => Port == 0 ? null : "http://" + CoreDeviceArguments.BindAddress + ":" + Port.ToString(CultureInfo.InvariantCulture) + "/";

        /// <summary>A session is running, stopping, or waiting for the previous one to stop.</summary>
        public bool IsActive => Phase != SessionPhase.Idle && Phase != SessionPhase.Stopped && Phase != SessionPhase.Failed;

        /// <summary>The last stop of serve-web had to kill it: its cleanup (camera and microphone release) did not finish.</summary>
        public bool LastStopWasForced { get; }

        public bool WaitingForPreviousStop { get; }

        public string StatusText =>
            WaitingForPreviousStop ? CoreDeviceMessages.WaitingForPreviousStop
            : Failure != SessionFailure.None ? CoreDeviceMessages.Failure(Failure)
            : CoreDeviceMessages.Phase(Phase);

        public bool Equals(SessionView other) =>
            other != null && Phase == other.Phase && Failure == other.Failure && Route == other.Route && string.Equals(Udid, other.Udid, StringComparison.Ordinal)
            && Port == other.Port && LastStopWasForced == other.LastStopWasForced && WaitingForPreviousStop == other.WaitingForPreviousStop;

        public override bool Equals(object obj) => Equals(obj as SessionView);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Phase;
                hash = hash * 31 + (int)Failure;
                hash = hash * 31 + (int)Route;
                hash = hash * 31 + (Udid == null ? 0 : StringComparer.Ordinal.GetHashCode(Udid));
                hash = hash * 31 + Port;
                hash = hash * 31 + (LastStopWasForced ? 1 : 0);
                return hash * 31 + (WaitingForPreviousStop ? 1 : 0);
            }
        }
    }
}

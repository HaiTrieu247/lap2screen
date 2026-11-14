using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MacExtend.Core.CoreDevice
{
    public enum GestureEnd
    {
        /// <summary>The gesture ran its course and lifted its own finger.</summary>
        Completed,

        /// <summary>serve-web answered too slowly: the gesture hit its deadline and the cleanup release lifted the finger.</summary>
        CutAtDeadline,
    }

    public sealed class GestureResult
    {
        public GestureResult(GestureEnd end, ReleaseOutcome cleanup)
        {
            End = end;
            Cleanup = cleanup;
        }

        public GestureEnd End { get; }

        /// <summary>What the cleanup release did; <see cref="ReleaseOutcome.NothingHeld"/> when the gesture lifted its own finger.</summary>
        public ReleaseOutcome Cleanup { get; }
    }

    /// <summary>
    /// Hold and drag on a <see cref="CoreDeviceClient"/>, timed by the clock rather than by the answers: a slow answer
    /// drops drag frames instead of stretching the gesture. The whole gesture, first contact to release, runs under a
    /// deadline of its duration plus <see cref="Slack"/>, so never more than 10 s; past it the gesture is cut, and
    /// whenever a finger may still be down, <see cref="CoreDeviceClient.ReleaseAllAsync"/> lifts it on its own deadline.
    /// </summary>
    public static class CoreDeviceGestures
    {
        public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(9);

        /// <summary>How far past its duration a gesture may run on slow answers before it is cut.</summary>
        public static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);

        public static readonly TimeSpan DragFrame = TimeSpan.FromMilliseconds(16);

        public static Task<GestureResult> HoldAsync(CoreDeviceClient client, int x, int y, TimeSpan duration, TimeSpan releaseDeadline, CancellationToken token)
        {
            Check(client, duration);
            CoreDeviceClient.CheckCoordinates(x, y);
            return RunAsync(client, duration, releaseDeadline, token, async (clock, limit) =>
            {
                await client.ContactAsync(x, y, limit).ConfigureAwait(false);
                var left = duration - clock.Elapsed;
                if (left > TimeSpan.Zero) await Task.Delay(left, limit).ConfigureAwait(false);
                await client.ReleaseContactAsync(x, y, limit).ConfigureAwait(false);
            });
        }

        /// <summary>A straight line from (x1, y1) to (x2, y2): one move per <see cref="DragFrame"/> on the clock, the last one at the end point.</summary>
        public static Task<GestureResult> DragAsync(CoreDeviceClient client, int x1, int y1, int x2, int y2, TimeSpan duration, TimeSpan releaseDeadline, CancellationToken token) =>
            DragAsync(client, x1, y1, x2, y2, duration, TimeSpan.Zero, releaseDeadline, token);

        /// <summary>
        /// The same drag, with the finger kept at the end point for <paramref name="rest"/> before it lifts: a list
        /// scrolled that way stops where it was dragged to instead of flinging on. The rest is always whole, after the
        /// last move; it counts towards the longest gesture allowed and towards the deadline.
        /// </summary>
        public static Task<GestureResult> DragAsync(CoreDeviceClient client, int x1, int y1, int x2, int y2, TimeSpan duration, TimeSpan rest, TimeSpan releaseDeadline, CancellationToken token)
        {
            if (rest < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(rest));
            Check(client, duration + rest);
            if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
            CoreDeviceClient.CheckCoordinates(x1, y1);
            CoreDeviceClient.CheckCoordinates(x2, y2);
            return RunAsync(client, duration + rest, releaseDeadline, token, async (clock, limit) =>
            {
                await client.ContactAsync(x1, y1, limit).ConfigureAwait(false);
                var total = duration.Ticks;
                var frame = DragFrame.Ticks;
                var due = 0L;
                while (due < total)
                {
                    // the next frame after the last one sent and after now: a slow answer skips frames, a timer that
                    // fires early never adds one
                    due = Math.Min(total, Math.Max(due + frame, (clock.Elapsed.Ticks / frame + 1) * frame));
                    var wait = due - clock.Elapsed.Ticks;
                    if (wait > 0) await Task.Delay(TimeSpan.FromTicks(wait), limit).ConfigureAwait(false);
                    await client.ContactAsync(Along(x1, x2, due, total), Along(y1, y2, due, total), limit).ConfigureAwait(false);
                }
                // the rest is counted from the last move, however late a slow answer made it: a rest eaten by a stall
                // would lift the finger mid-move and let the list fling on
                if (rest > TimeSpan.Zero) await Task.Delay(rest, limit).ConfigureAwait(false);
                await client.ReleaseContactAsync(x2, y2, limit).ConfigureAwait(false);
            });
        }

        private static async Task<GestureResult> RunAsync(CoreDeviceClient client, TimeSpan duration, TimeSpan releaseDeadline, CancellationToken token, Func<Stopwatch, CancellationToken, Task> gesture)
        {
            var end = GestureEnd.Completed;
            var cleanup = ReleaseOutcome.NothingHeld;
            try
            {
                using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    limit.CancelAfter(duration + Slack);
                    try
                    {
                        await gesture(Stopwatch.StartNew(), limit.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (limit.IsCancellationRequested && !token.IsCancellationRequested)
                    {
                        // if the cut caught a request in flight, the client has closed itself: that request's fate is unknown
                        end = GestureEnd.CutAtDeadline;
                    }
                }
            }
            finally
            {
                // also when the gesture failed or was stopped: the caller's token may be cancelled, this one has its own deadline
                if (client.ContactMayBeHeld) cleanup = await client.ReleaseAllAsync(releaseDeadline).ConfigureAwait(false);
            }
            return new GestureResult(end, cleanup);
        }

        private static void Check(CoreDeviceClient client, TimeSpan duration)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (duration <= TimeSpan.Zero || duration > MaxDuration)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), $"gestures last from 1 to {MaxDuration.TotalMilliseconds:0} ms");
            }
        }

        private static int Along(int from, int to, long at, long total) => (int)(from + (long)(to - from) * at / total);
    }
}

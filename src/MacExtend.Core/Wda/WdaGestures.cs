using System;
using System.Collections.Generic;

namespace MacExtend.Core.Wda
{
    /// <summary>A point of a drag: milliseconds since the button went down, and where, as a fraction (0..1) of the picture.</summary>
    public struct PathSample
    {
        public PathSample(int timeMs, double x, double y)
        {
            TimeMs = timeMs;
            X = x;
            Y = y;
        }

        public int TimeMs { get; }
        public double X { get; }
        public double Y { get; }
    }

    /// <summary>
    /// Mouse gestures as WDA touch actions, in points of the device's current orientation. Every action presses and lifts
    /// in one request, so no finger is ever left down on the device whatever happens to the connection.
    /// </summary>
    public static class WdaGestures
    {
        public const int TapHoldMs = 40;
        public const int LongPressHoldMs = 700;
        /// <summary>A drag never lasts longer than this on the device; a longer one is replayed faster, with the same shape.</summary>
        public const int MaxPathMs = 3000;
        /// <summary>Moves of one drag; more samples are thinned out evenly.</summary>
        public const int MaxPathMoves = 60;
        /// <summary>Samples closer together than this are merged.</summary>
        public const int MinSampleGapMs = 16;
        /// <summary>A still finger at the end longer than this stops a scroll view from flinging; kept at most this long.</summary>
        public const int MaxEndHoldMs = 500;

        public static IReadOnlyList<TouchStep> Tap(double x, double y) => Press(x, y, TapHoldMs);

        public static IReadOnlyList<TouchStep> LongPress(double x, double y) => Press(x, y, LongPressHoldMs);

        /// <summary>Down at the start, straight to the end in <paramref name="durationMs"/>, a still finger for <paramref name="holdEndMs"/>, up.</summary>
        public static IReadOnlyList<TouchStep> Swipe(double x1, double y1, double x2, double y2, int durationMs, int holdEndMs)
        {
            var steps = new List<TouchStep>
            {
                TouchStep.Move(x1, y1, 0),
                TouchStep.Down(),
                TouchStep.Move(x2, y2, Math.Max(20, durationMs)),
            };
            if (holdEndMs > 0) steps.Add(TouchStep.Pause(holdEndMs));
            steps.Add(TouchStep.Up());
            return steps;
        }

        /// <summary>
        /// A drag replayed along the mouse's own path, in its own rhythm (squeezed into <see cref="MaxPathMs"/> when it was
        /// longer). <paramref name="releaseMs"/> is when the button came up: a pointer that stood still before that keeps the
        /// finger still (no fling), one released while moving lets the content fly on.
        /// </summary>
        /// <param name="width">Screen width in points; the path is kept on the screen, as WDA refuses points off it.</param>
        public static IReadOnlyList<TouchStep> Path(IReadOnlyList<PathSample> samples, int releaseMs, double width, double height)
        {
            if (samples == null || samples.Count == 0) throw new ArgumentException("no samples", nameof(samples));
            var kept = Thin(samples);
            var last = kept[kept.Count - 1].TimeMs;
            var hold = Math.Min(MaxEndHoldMs, Math.Max(0, releaseMs - last));
            var total = last - kept[0].TimeMs;
            var scale = total > MaxPathMs ? (double)MaxPathMs / total : 1.0;

            var steps = new List<TouchStep> { TouchStep.Move(X(kept[0].X, width), Y(kept[0].Y, height), 0), TouchStep.Down() };
            var previous = kept[0].TimeMs;
            var carried = 0.0;
            for (var i = 1; i < kept.Count; i++)
            {
                // the rounding rest moves on to the next step, so the whole replay keeps its length
                var exact = (kept[i].TimeMs - previous) * scale + carried;
                var ms = (int)Math.Floor(exact);
                carried = exact - ms;
                previous = kept[i].TimeMs;
                steps.Add(TouchStep.Move(X(kept[i].X, width), Y(kept[i].Y, height), ms));
            }
            if (kept.Count == 1) steps.Add(TouchStep.Move(X(kept[0].X, width), Y(kept[0].Y, height), 20));
            if (hold > 0) steps.Add(TouchStep.Pause(hold));
            steps.Add(TouchStep.Up());
            return steps;
        }

        /// <summary>The samples WDA gets: none closer than <see cref="MinSampleGapMs"/>, at most <see cref="MaxPathMoves"/> + 1, first and last always.</summary>
        internal static IReadOnlyList<PathSample> Thin(IReadOnlyList<PathSample> samples)
        {
            var spaced = new List<PathSample> { samples[0] };
            for (var i = 1; i < samples.Count; i++)
            {
                var gap = samples[i].TimeMs - spaced[spaced.Count - 1].TimeMs;
                if (gap < 0) continue;
                if (gap >= MinSampleGapMs) spaced.Add(samples[i]);
                else if (i == samples.Count - 1 && spaced.Count > 1) spaced[spaced.Count - 1] = samples[i];
                else if (i == samples.Count - 1) spaced.Add(samples[i]);
            }
            if (spaced.Count <= MaxPathMoves + 1) return spaced;
            var thinned = new List<PathSample>(MaxPathMoves + 1);
            for (var k = 0; k <= MaxPathMoves; k++) thinned.Add(spaced[(int)Math.Round((double)k * (spaced.Count - 1) / MaxPathMoves)]);
            return thinned;
        }

        private static IReadOnlyList<TouchStep> Press(double x, double y, int holdMs) => new[]
        {
            TouchStep.Move(x, y, 0),
            TouchStep.Down(),
            TouchStep.Pause(holdMs),
            TouchStep.Up(),
        };

        /// <summary>A fraction of the picture as a point WDA accepts: on the screen, never on its far edge.</summary>
        public static double X(double fraction, double width) => Clamp(fraction * width, width);

        public static double Y(double fraction, double height) => Clamp(fraction * height, height);

        private static double Clamp(double value, double size) => Math.Min(Math.Max(value, 0.0), Math.Max(0.0, size - 1));
    }

    /// <summary>
    /// Mouse wheel and touchpad scrolling as short drags under the pointer. A notch moves the content about an eighth of
    /// the screen; small touchpad steps add up until they are a drag the device does not take for a tap. Each drag ends
    /// with a still finger, so one notch always scrolls the same distance instead of flinging.
    /// </summary>
    public sealed class WdaScroll
    {
        public const double PerNotch = 0.12;
        public const double MaxStep = 0.45;
        public const int MaxSteps = 3;
        /// <summary>A shorter drag could be taken for a tap.</summary>
        public const double MinPoints = 24;
        public const int DragMs = 150;
        public const int HoldMs = 60;
        /// <summary>A rest older than this, or one in the other direction, is dropped.</summary>
        public static readonly TimeSpan CarryAge = TimeSpan.FromSeconds(1);

        private double _carry;
        private bool _carryHorizontal;
        private DateTime _carryAt;

        /// <summary>
        /// The drags for <paramref name="notches"/> wheel notches at (<paramref name="xf"/>, <paramref name="yf"/>): positive
        /// is the wheel turned away from the user (vertical: the content moves down, the finger drags down) or tilted right
        /// (horizontal: the content moves left, the finger drags left). Empty while the steps do not add up to a drag yet.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<TouchStep>> Drags(double xf, double yf, double notches, bool horizontal, double width, double height, DateTime now)
        {
            if (horizontal != _carryHorizontal || now - _carryAt > CarryAge || _carry * notches < 0) _carry = 0;
            notches += _carry;
            var size = horizontal ? width : height;
            var perNotch = PerNotch * size;
            var total = notches * perNotch * (horizontal ? -1 : 1);
            var drags = new List<IReadOnlyList<TouchStep>>();
            _carryHorizontal = horizontal;
            _carryAt = now;
            if (Math.Abs(total) < MinPoints)
            {
                _carry = notches;
                return drags;
            }

            // away from the edges: an edge drag is "back" or a system gesture
            var x = Math.Min(Math.Max(xf * width, width * 0.15), width * 0.85);
            var y = Math.Min(Math.Max(yf * height, height * 0.15), height * 0.85);
            for (var i = 0; i < MaxSteps && Math.Abs(total) >= MinPoints; i++)
            {
                var d = Math.Max(-MaxStep * size, Math.Min(MaxStep * size, total));
                if (horizontal)
                {
                    var x1 = Math.Min(Math.Max(x, width * 0.15 + Math.Max(0, -d)), width * 0.85 - Math.Max(0, d));
                    drags.Add(WdaGestures.Swipe(x1, y, x1 + d, y, DragMs, HoldMs));
                }
                else
                {
                    var y1 = Math.Min(Math.Max(y, height * 0.15 + Math.Max(0, -d)), height * 0.85 - Math.Max(0, d));
                    drags.Add(WdaGestures.Swipe(x, y1, x, y1 + d, DragMs, HoldMs));
                }
                total -= d;
            }
            // a rest too short for a drag waits for the next notch; more than MaxSteps drags' worth is dropped
            _carry = Math.Abs(total) < MinPoints ? total / perNotch * (horizontal ? -1 : 1) : 0;
            return drags;
        }
    }
}

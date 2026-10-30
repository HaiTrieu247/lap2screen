using System;
using System.Collections.Generic;
using System.Globalization;

namespace MacExtend.Core
{
    /// <summary>
    /// A window's position and size in WPF units, kept in the settings as "x,y,w,h" (whole units, invariant culture).
    /// The saved text is opaque until <see cref="TryParse"/> accepts it; anything else means the default position.
    /// </summary>
    public struct WindowBounds : IEquatable<WindowBounds>
    {
        public WindowBounds(double left, double top, double width, double height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public double Left { get; }
        public double Top { get; }
        public double Width { get; }
        public double Height { get; }
        public double Right => Left + Width;
        public double Bottom => Top + Height;

        /// <summary>Four finite numbers separated by commas, with a positive width and height.</summary>
        public static bool TryParse(string text, out WindowBounds bounds)
        {
            bounds = default(WindowBounds);
            var parts = (text ?? "").Split(',');
            if (parts.Length != 4) return false;
            var values = new double[4];
            for (var i = 0; i < 4; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return false;
                if (double.IsNaN(values[i]) || double.IsInfinity(values[i])) return false;
            }
            if (values[2] <= 0 || values[3] <= 0) return false;
            bounds = new WindowBounds(values[0], values[1], values[2], values[3]);
            return true;
        }

        /// <summary>
        /// Where to open a window saved as <paramref name="saved"/>: on the screen showing most of it, moved fully inside
        /// that screen's work area and sized between the window's minimum and the work area. Null (the default position)
        /// when nothing usable was saved or no current screen shows any of it (a monitor that was unplugged).
        /// </summary>
        /// <param name="workAreas">Every screen's work area, in the same units as the saved bounds.</param>
        public static WindowBounds? Place(string saved, IReadOnlyList<WindowBounds> workAreas, double minWidth, double minHeight)
        {
            if (!TryParse(saved, out var bounds) || workAreas == null) return null;
            WindowBounds? screen = null;
            var shown = 0.0;
            foreach (var work in workAreas)
            {
                if (work.Width <= 0 || work.Height <= 0) continue;
                var area = Overlap(bounds.Left, bounds.Right, work.Left, work.Right) * Overlap(bounds.Top, bounds.Bottom, work.Top, work.Bottom);
                if (area <= shown) continue;
                shown = area;
                screen = work;
            }
            if (screen == null) return null;

            var on = screen.Value;
            var width = Math.Max(minWidth, Math.Min(bounds.Width, on.Width));
            var height = Math.Max(minHeight, Math.Min(bounds.Height, on.Height));
            return new WindowBounds(Inside(bounds.Left, on.Left, on.Right - width), Inside(bounds.Top, on.Top, on.Bottom - height), width, height);
        }

        /// <summary>"x,y,w,h" in whole units, whatever the culture (Vietnamese writes a decimal comma).</summary>
        public string Format() => Whole(Left) + "," + Whole(Top) + "," + Whole(Width) + "," + Whole(Height);

        public bool Equals(WindowBounds other) =>
            Left.Equals(other.Left) && Top.Equals(other.Top) && Width.Equals(other.Width) && Height.Equals(other.Height);

        public override bool Equals(object obj) => obj is WindowBounds other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Left.GetHashCode();
                hash = hash * 31 + Top.GetHashCode();
                hash = hash * 31 + Width.GetHashCode();
                return hash * 31 + Height.GetHashCode();
            }
        }

        public override string ToString() => Format();

        private static double Overlap(double start, double end, double otherStart, double otherEnd) =>
            Math.Max(0, Math.Min(end, otherEnd) - Math.Max(start, otherStart));

        /// <summary>From <paramref name="min"/> to <paramref name="max"/>; <paramref name="min"/> wins when the window is wider than the screen.</summary>
        private static double Inside(double value, double min, double max) => Math.Max(min, Math.Min(value, max));

        // a long, so -0.3 never prints as "-0"
        private static string Whole(double value) => ((long)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
    }
}

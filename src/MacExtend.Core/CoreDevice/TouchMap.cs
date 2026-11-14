using System;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>The way the device's interface is turned, as SpringBoard reports it (UIInterfaceOrientation).</summary>
    public enum ScreenOrientation
    {
        Unknown,
        Portrait,
        PortraitUpsideDown,
        /// <summary>Turned a quarter left from portrait: the home button, or the home indicator, on the right (UIKit's LandscapeRight, 3).</summary>
        LandscapeHomeRight,
        /// <summary>Turned a quarter right: the home side on the left (UIKit's LandscapeLeft, 4).</summary>
        LandscapeHomeLeft,
    }

    /// <summary>Whether the mirrored picture is taller or wider, from the size uxplay logged.</summary>
    public enum PictureShape
    {
        Unknown,
        Portrait,
        Landscape,
    }

    /// <summary>
    /// A point on the AirPlay picture to serve-web's touch coordinates. The picture is the interface upright, turned the
    /// way the device is; serve-web hands /touch straight to the digitizer, whose 0..65535 axes run along the panel in its
    /// own portrait orientation whatever the interface does. So the point is turned back by the interface orientation.
    /// </summary>
    public static class TouchMap
    {
        /// <summary>
        /// `pymobiledevice3 springboard orientation`: an InterfaceOrientation, printed as its number by Python 3.11 and
        /// later (1 portrait, 2 upside down, 3 landscape, 4 landscape home to the left), as its name before. The last
        /// non-empty line counts; anything else is unknown.
        /// </summary>
        public static ScreenOrientation ParseOrientation(string stdout)
        {
            var line = (stdout ?? "").Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
            var dot = line.LastIndexOf('.');
            var word = dot >= 0 ? line.Substring(dot + 1) : line;
            switch (word)
            {
                case "1":
                case "PORTRAIT":
                    return ScreenOrientation.Portrait;
                case "2":
                case "PORTRAIT_UPSIDE_DOWN":
                    return ScreenOrientation.PortraitUpsideDown;
                case "3":
                case "LANDSCAPE":
                    return ScreenOrientation.LandscapeHomeRight;
                case "4":
                case "LANDSCAPE_HOME_TO_LEFT":
                    return ScreenOrientation.LandscapeHomeLeft;
                default:
                    return ScreenOrientation.Unknown;
            }
        }

        public static PictureShape Shape(int pictureWidth, int pictureHeight) =>
            pictureWidth <= 0 || pictureHeight <= 0 ? PictureShape.Unknown
            : pictureWidth > pictureHeight ? PictureShape.Landscape
            : PictureShape.Portrait;

        /// <summary>
        /// The orientation to map with. The picture's shape is certain and the answer may be stale or missing: an answer
        /// that contradicts the shape gives way to it. A landscape picture without a landscape answer is taken as home on
        /// the right, the way apps turn first.
        /// </summary>
        public static ScreenOrientation Effective(ScreenOrientation reported, PictureShape shape)
        {
            var landscape = reported == ScreenOrientation.LandscapeHomeRight || reported == ScreenOrientation.LandscapeHomeLeft;
            switch (shape)
            {
                case PictureShape.Landscape:
                    return landscape ? reported : ScreenOrientation.LandscapeHomeRight;
                case PictureShape.Portrait:
                    return reported == ScreenOrientation.PortraitUpsideDown ? reported : ScreenOrientation.Portrait;
                default:
                    return reported == ScreenOrientation.Unknown ? ScreenOrientation.Portrait : reported;
            }
        }

        /// <param name="xf">Left to right across the upright picture, 0..1 (clamped).</param>
        /// <param name="yf">Top to bottom, 0..1 (clamped).</param>
        public static void ToHid(double xf, double yf, ScreenOrientation orientation, out int x, out int y)
        {
            xf = Clamp01(xf);
            yf = Clamp01(yf);
            double u, v;
            switch (orientation)
            {
                case ScreenOrientation.PortraitUpsideDown:
                    u = 1 - xf;
                    v = 1 - yf;
                    break;
                case ScreenOrientation.LandscapeHomeRight:
                    // the panel's top is the picture's left edge, the panel's left edge its bottom
                    u = 1 - yf;
                    v = xf;
                    break;
                case ScreenOrientation.LandscapeHomeLeft:
                    // the panel's top is the picture's right edge, the panel's left edge its top
                    u = yf;
                    v = 1 - xf;
                    break;
                default:
                    u = xf;
                    v = yf;
                    break;
            }
            x = Scale(u);
            y = Scale(v);
        }

        private static int Scale(double f) => (int)Math.Round(Clamp01(f) * CoreDeviceClient.MaxCoordinate);

        private static double Clamp01(double v) => double.IsNaN(v) || v < 0 ? 0 : v > 1 ? 1 : v;
    }

    /// <summary>
    /// A wheel turn as one short drag on the picture, in picture fractions. A positive turn is the wheel turned away from
    /// the user (vertical: the content moves down, so the finger drags down) or tilted right (horizontal: the content
    /// moves left, the finger drags left). The finger rests a moment at the end so the list stops instead of flinging on.
    /// </summary>
    public sealed class ScrollDrag
    {
        public const double PerNotch = 0.12;
        public const double MinDistance = 0.04;
        public const double MaxDistance = 0.45;
        /// <summary>The drag stays this far inside the picture's edges, where iOS has its own edge gestures.</summary>
        public const double Margin = 0.03;
        public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(150);
        public static readonly TimeSpan Rest = TimeSpan.FromMilliseconds(60);

        private ScrollDrag(double x1, double y1, double x2, double y2)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }

        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }

        /// <param name="notches">Wheel notches, a fraction allowed (a precision touchpad); the sign gives the direction.</param>
        public static ScrollDrag For(double xf, double yf, double notches, bool horizontal)
        {
            var distance = Math.Min(MaxDistance, Math.Max(MinDistance, Math.Abs(notches) * PerNotch));
            // vertical: positive drags down; horizontal: positive drags left
            var sign = (notches >= 0 ? 1 : -1) * (horizontal ? -1 : 1);
            var start = Fit(horizontal ? xf : yf, sign * distance);
            var end = start + sign * distance;
            var across = Math.Min(1 - Margin, Math.Max(Margin, horizontal ? yf : xf));
            return horizontal ? new ScrollDrag(start, across, end, across) : new ScrollDrag(across, start, across, end);
        }

        /// <summary>The start that keeps a drag of <paramref name="delta"/> inside the margins, as near <paramref name="at"/> as can be.</summary>
        private static double Fit(double at, double delta)
        {
            var low = Margin - Math.Min(0, delta);
            var high = 1 - Margin - Math.Max(0, delta);
            return Math.Min(high, Math.Max(low, at));
        }
    }
}

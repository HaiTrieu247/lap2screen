using System;

namespace MacExtend.Core
{
    /// <summary>A rectangle in physical pixels, right and bottom edges exclusive like a Win32 RECT.</summary>
    public struct PixelRect : IEquatable<PixelRect>
    {
        public PixelRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public int CenterX => Left + Width / 2;
        public int CenterY => Top + Height / 2;

        /// <summary>Right and bottom are exclusive, as in a Win32 RECT.</summary>
        public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

        public bool Equals(PixelRect other) =>
            Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

        public override bool Equals(object obj) => obj is PixelRect other && Equals(other);

        public override int GetHashCode() => ((Left * 397 ^ Top) * 397 ^ Right) * 397 ^ Bottom;

        public static bool operator ==(PixelRect a, PixelRect b) => a.Equals(b);

        public static bool operator !=(PixelRect a, PixelRect b) => !a.Equals(b);

        public override string ToString() => "(" + Left + "," + Top + ")-(" + Right + "," + Bottom + ")";
    }

    /// <summary>
    /// Where uxplay's video window goes the first time it settles as a normal window. d3d11videosink
    /// sizes it to the picture plus its frame, which is taller than the work area on a laptop showing
    /// a picture of its own resolution.
    /// </summary>
    public static class WindowFit
    {
        /// <summary>
        /// A window larger than the work area is scaled down as a whole, keeping its proportions (the sink
        /// letterboxes the picture inside), and centred. A window that already fits only moves back inside
        /// the work area. A window is never made larger.
        /// </summary>
        /// <param name="window">Window rectangle, frame included.</param>
        /// <param name="work">Work area of the monitor showing the window, in the same coordinates.</param>
        public static PixelRect Fit(PixelRect window, PixelRect work)
        {
            if (window.Width <= 0 || window.Height <= 0 || work.Width <= 0 || work.Height <= 0) return window;

            if (window.Width > work.Width || window.Height > work.Height)
            {
                int width, height;
                // scale = min(work.Width / window.Width, work.Height / window.Height), rounded down exactly
                if ((long)work.Width * window.Height <= (long)work.Height * window.Width)
                {
                    width = work.Width;
                    height = (int)((long)window.Height * work.Width / window.Width);
                }
                else
                {
                    height = work.Height;
                    width = (int)((long)window.Width * work.Height / window.Height);
                }
                width = Math.Max(1, width);
                height = Math.Max(1, height);
                var left = work.Left + (work.Width - width) / 2;
                var top = work.Top + (work.Height - height) / 2;
                return new PixelRect(left, top, left + width, top + height);
            }

            var x = Math.Max(work.Left, Math.Min(window.Left, work.Right - window.Width));
            var y = Math.Max(work.Top, Math.Min(window.Top, work.Bottom - window.Height));
            return new PixelRect(x, y, x + window.Width, y + window.Height);
        }
    }
}

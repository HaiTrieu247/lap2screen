using System;
using System.Globalization;

namespace MacExtend.Core
{
    public readonly struct DisplayMode : IEquatable<DisplayMode>
    {
        public DisplayMode(int width, int height, int refreshHz)
        {
            Width = width;
            Height = height;
            RefreshHz = refreshHz;
        }

        public int Width { get; }
        public int Height { get; }
        public int RefreshHz { get; }

        /// <summary>uxplay "-s" syntax.</summary>
        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0}x{1}@{2}", Width, Height, RefreshHz);

        public bool Equals(DisplayMode other) =>
            Width == other.Width && Height == other.Height && RefreshHz == other.RefreshHz;

        public override bool Equals(object obj) => obj is DisplayMode other && Equals(other);

        public override int GetHashCode() => (Width * 397 ^ Height) * 397 ^ RefreshHz;

        public static bool operator ==(DisplayMode a, DisplayMode b) => a.Equals(b);

        public static bool operator !=(DisplayMode a, DisplayMode b) => !a.Equals(b);
    }

    public sealed class RequestedVideo
    {
        public RequestedVideo(DisplayMode mode, bool h265)
        {
            Mode = mode;
            H265 = h265;
        }

        public DisplayMode Mode { get; }
        public bool H265 { get; }
    }
}

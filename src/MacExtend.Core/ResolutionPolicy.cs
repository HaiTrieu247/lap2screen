namespace MacExtend.Core
{
    /// <summary>Decides which display mode MacExtend reports to the Mac.</summary>
    public static class ResolutionPolicy
    {
        // H.264 AirPlay mirroring is a 1080p-class stream; larger sizes need H.265.
        private const int AutoMaxWidth = 1920, AutoMaxHeight = 1080, AutoMaxRefresh = 60;
        private const int MinUsableRefresh = 24;

        public static RequestedVideo Resolve(AppSettings settings, DisplayMode native)
        {
            if (settings.ResolutionMode == ResolutionMode.Custom)
            {
                var s = settings.Clone();
                s.Clamp();
                return new RequestedVideo(new DisplayMode(s.CustomWidth, s.CustomHeight, s.CustomRefresh), s.CustomH265);
            }

            var refresh = native.RefreshHz < MinUsableRefresh ? AutoMaxRefresh : System.Math.Min(native.RefreshHz, AutoMaxRefresh);
            if (native.Width <= 0 || native.Height <= 0)
            {
                return new RequestedVideo(new DisplayMode(AutoMaxWidth, AutoMaxHeight, refresh), false);
            }

            long w = native.Width, h = native.Height;
            if (w > AutoMaxWidth || h > AutoMaxHeight)
            {
                // Integer math keeps the aspect ratio exact: compare w/h against 1920/1080.
                if (w * AutoMaxHeight > h * AutoMaxWidth)
                {
                    h = h * AutoMaxWidth / w;
                    w = AutoMaxWidth;
                }
                else
                {
                    w = w * AutoMaxHeight / h;
                    h = AutoMaxHeight;
                }
            }

            return new RequestedVideo(new DisplayMode(Even(w), Even(h), refresh), false);
        }

        private static int Even(long value) => (int)(value & ~1L);
    }
}

using System;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// Where the device's screen is inside uxplay's video window. d3d11videosink keeps the picture's aspect ratio, so the
    /// screen is the largest rectangle with the picture's aspect (from the size uxplay logs, in the current orientation)
    /// centred in the client area; the rest is black bars that belong to nothing.
    /// </summary>
    public static class PictureMap
    {
        /// <summary>The device screen inside <paramref name="client"/>; the whole client area while the aspect is unknown.</summary>
        public static PixelRect Picture(PixelRect client, double deviceWidth, double deviceHeight)
        {
            if (client.Width <= 0 || client.Height <= 0 || !(deviceWidth > 0) || !(deviceHeight > 0)) return client;
            var aspect = deviceWidth / deviceHeight;
            int width, height;
            if ((double)client.Width / client.Height > aspect)
            {
                height = client.Height;
                width = Math.Max(1, (int)Math.Round(height * aspect));
            }
            else
            {
                width = client.Width;
                height = Math.Max(1, (int)Math.Round(width / aspect));
            }
            var left = client.Left + (client.Width - width) / 2;
            var top = client.Top + (client.Height - height) / 2;
            return new PixelRect(left, top, left + width, top + height);
        }

        /// <summary>
        /// A screen point as fractions (0..1) of <paramref name="picture"/>, measured at the pixel's centre. false (and the
        /// fractions clamped to the picture) when the point is outside it: a drag that leaves the picture still ends on its edge.
        /// </summary>
        public static bool ToFraction(PixelRect picture, int x, int y, out double xf, out double yf)
        {
            if (picture.Width <= 0 || picture.Height <= 0)
            {
                xf = yf = 0;
                return false;
            }
            xf = Clamp01((x - picture.Left + 0.5) / picture.Width);
            yf = Clamp01((y - picture.Top + 0.5) / picture.Height);
            return picture.Contains(x, y);
        }

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MacExtend.Core
{
    public enum LogEventKind
    {
        None,
        ClientConnecting,
        ConnectionLost,
        Error,
        /// <summary>The user closed the video window. uxplay drops the client and waits for the next one.</summary>
        VideoWindowClosed,
        /// <summary>The mirrored picture's size, at the start and again after the device rotates.</summary>
        PictureSize,
    }

    public sealed class LogEvent
    {
        public static readonly LogEvent None = new LogEvent(LogEventKind.None, null, null, null);

        public LogEvent(LogEventKind kind, string clientName, string clientModel, string text)
        {
            Kind = kind;
            ClientName = clientName;
            ClientModel = clientModel;
            Text = text;
        }

        private LogEvent(int pictureWidth, int pictureHeight, string text) : this(LogEventKind.PictureSize, null, null, text)
        {
            PictureWidth = pictureWidth;
            PictureHeight = pictureHeight;
        }

        public static LogEvent Picture(int width, int height, string text) => new LogEvent(width, height, text);

        public LogEventKind Kind { get; }
        public string ClientName { get; }
        public string ClientModel { get; }
        public string Text { get; }
        /// <summary>The picture's size in pixels for <see cref="LogEventKind.PictureSize"/>; 0 otherwise.</summary>
        public int PictureWidth { get; }
        public int PictureHeight { get; }
    }

    /// <summary>Recognises the few uxplay output lines MacExtend reacts to.</summary>
    public static class UxPlayLogParser
    {
        // uxplay.cpp report_client_request(): "connection request from %s (%s) with deviceID = %s"
        private static readonly Regex ConnectionRequest = new Regex(
            @"^connection request from (?<name>.*) \((?<model>[^()]*)\) with deviceID = ",
            RegexOptions.CultureInvariant);

        // renderers/video_renderer.c: "GStreamer error (video): %s %s" with the element name and the error.
        // d3d11videosink reports a window closed with X or Alt+F4 as "Output window was closed".
        private const string VideoErrorPrefix = "GStreamer error (video):";
        private const string WindowClosedMessage = "Output window was closed";

        // lib/raop_rtp_mirror.c, at every codec configuration (the start, each rotation), raised from debug to info by
        // native/build-uxplay.sh: "raop_rtp_mirror width_source = %f height_source = %f x_offset = %f y_offset = %f
        // width = %f height = %f". width and height are the encoded picture; the source size when they are 0.
        private static readonly Regex PictureSizeLine = new Regex(
            @"raop_rtp_mirror width_source = (?<ws>\d+(?:\.\d+)?) height_source = (?<hs>\d+(?:\.\d+)?) x_offset = [-\d.]+ y_offset = [-\d.]+ width = (?<w>\d+(?:\.\d+)?) height = (?<h>\d+(?:\.\d+)?)",
            RegexOptions.CultureInvariant);

        /// <summary>A picture side larger than this is not a real size.</summary>
        private const int MaxPictureSide = 16384;

        public static LogEvent Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return LogEvent.None;

            var m = ConnectionRequest.Match(line);
            if (m.Success)
            {
                return new LogEvent(LogEventKind.ClientConnecting, m.Groups["name"].Value, m.Groups["model"].Value, line);
            }
            if (line.Contains("lost connection with client"))
            {
                return new LogEvent(LogEventKind.ConnectionLost, null, null, line);
            }
            if (line.StartsWith("*** ERROR"))
            {
                return new LogEvent(LogEventKind.Error, null, null, line);
            }
            if (line.StartsWith(VideoErrorPrefix, StringComparison.Ordinal) &&
                line.IndexOf(WindowClosedMessage, VideoErrorPrefix.Length, StringComparison.Ordinal) >= 0)
            {
                return new LogEvent(LogEventKind.VideoWindowClosed, null, null, line);
            }
            var size = PictureSizeLine.Match(line);
            if (size.Success)
            {
                var width = Side(size.Groups["w"].Value);
                var height = Side(size.Groups["h"].Value);
                if (width == 0 || height == 0)
                {
                    width = Side(size.Groups["ws"].Value);
                    height = Side(size.Groups["hs"].Value);
                }
                if (width > 0 && height > 0) return LogEvent.Picture(width, height, line);
            }
            return LogEvent.None;
        }

        /// <summary>A whole number of pixels from uxplay's "%f"; 0 when it is none or out of range.</summary>
        private static int Side(string value)
        {
            if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)) return 0;
            var side = (int)Math.Round(d);
            return side >= 1 && side <= MaxPictureSide ? side : 0;
        }
    }
}

using System;
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

        public LogEventKind Kind { get; }
        public string ClientName { get; }
        public string ClientModel { get; }
        public string Text { get; }
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
            return LogEvent.None;
        }
    }
}

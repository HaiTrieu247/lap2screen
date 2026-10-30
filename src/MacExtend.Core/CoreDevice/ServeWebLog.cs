using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MacExtend.Core.CoreDevice
{
    public enum ServeWebLogKind
    {
        /// <summary>Anything not listed below. The probe never shows or stores these lines.</summary>
        Other,

        /// <summary>"Open http://127.0.0.1:PORT/ ... Ctrl-C to stop." — logged right after the HTTP listener is up.</summary>
        Listening,
        CodecString,
        HidWorkerStarted,
        StreamStartDelayed,
        DeviceDisconnected,
        DeviceReconnected,

        /// <summary>A cleanup step timed out or raised; stop_all_streams is the one that frees camera and microphone.</summary>
        ShutdownStepFailed,
        ShutdownComplete,

        /// <summary>Camera or microphone held by a foreground app: iOS refuses the mirroring session (code 9022).</summary>
        MediaInUse,

        /// <summary>"Failed to start service": usually Developer Mode off or no Developer Disk Image mounted.</summary>
        ServiceUnavailable,
        DeviceNotFound,
        Error,
    }

    public struct ServeWebLogLine
    {
        public ServeWebLogKind Kind;

        /// <summary>The port of a <see cref="ServeWebLogKind.Listening"/> line.</summary>
        public int Port;

        /// <summary>
        /// A value from a fixed vocabulary (a codec string, a cleanup step name) or null. Never free text from the
        /// line: upstream lines can carry the host name, the UDID and, at debug level, request bodies.
        /// </summary>
        public string Detail;
    }

    /// <summary>
    /// Classifies serve-web's stderr. pymobiledevice3 logs through ansilogs as
    /// "&lt;time&gt; &lt;host&gt; &lt;logger&gt;[&lt;pid&gt;] &lt;LEVEL&gt; &lt;message&gt;".
    /// </summary>
    public static class ServeWebLog
    {
        private const string StreamLogger = @"pymobiledevice3\.remote\.core_device\.screen_stream";

        private static readonly Regex Ansi = new Regex(@"\x1b\[[0-9;]*m", RegexOptions.CultureInvariant);

        private static readonly Regex Listening = Line(LogPrefix.Build(StreamLogger, "INFO") + @"Open http://127\.0\.0\.1:(\d{1,5})/ in Safari/Chrome\. Ctrl-C to stop\.\s*\z");
        private static readonly Regex Codec = Line(LogPrefix.Build(StreamLogger, "INFO") + @"WebCodecs codec string: ((?:hvc1|hev1)[A-Za-z0-9.]{0,60})\s*\z");
        private static readonly Regex HidWorker = Line(LogPrefix.Build(StreamLogger, "INFO") + @"hid worker started\s*\z");
        private static readonly Regex StreamDelayed = Line(LogPrefix.Build(StreamLogger, "WARNING") + @"initial stream start timed out");
        private static readonly Regex Disconnected = Line(LogPrefix.Build(StreamLogger, "WARNING") + @"device \S+ disconnected; waiting for tunneld");
        private static readonly Regex Reconnected = Line(LogPrefix.Build(StreamLogger, "INFO") + @"device \S+ back online via tunneld");
        private static readonly Regex StepFailed = Line(LogPrefix.Build(StreamLogger, "(?:WARNING|ERROR)") + @"shutdown: (\S+) (?:timed out after|raised)");
        private static readonly Regex Complete = Line(LogPrefix.Build(StreamLogger, "INFO") + @"shutdown complete\s*\z");
        private static readonly Regex Fatal = Line(LogPrefix.Build(LogPrefix.Main, "ERROR") + @"(.*)\z");

        // the cleanup steps serve-web's shutdown names (screen_stream.py, ScreenStreamServer.serve)
        private static readonly string[] Steps =
        {
            "_stop_hid", "_stop_active_stream", "_stop_audio_stream", "stop_all_streams", "_stop_clipboard_monitor", "_stop_accessibility",
        };

        public static ServeWebLogLine Parse(string line)
        {
            var result = new ServeWebLogLine { Kind = ServeWebLogKind.Other };
            if (string.IsNullOrEmpty(line)) return result;
            line = Ansi.Replace(line, "");

            Match m;
            if ((m = Listening.Match(line)).Success)
            {
                var port = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (port >= 1 && port <= 65535)
                {
                    result.Kind = ServeWebLogKind.Listening;
                    result.Port = port;
                }
            }
            else if ((m = Codec.Match(line)).Success)
            {
                result.Kind = ServeWebLogKind.CodecString;
                result.Detail = m.Groups[1].Value;
            }
            else if (HidWorker.IsMatch(line)) result.Kind = ServeWebLogKind.HidWorkerStarted;
            else if (StreamDelayed.IsMatch(line)) result.Kind = ServeWebLogKind.StreamStartDelayed;
            else if (Disconnected.IsMatch(line)) result.Kind = ServeWebLogKind.DeviceDisconnected;
            else if (Reconnected.IsMatch(line)) result.Kind = ServeWebLogKind.DeviceReconnected;
            else if ((m = StepFailed.Match(line)).Success)
            {
                result.Kind = ServeWebLogKind.ShutdownStepFailed;
                result.Detail = Array.IndexOf(Steps, m.Groups[1].Value) >= 0 ? m.Groups[1].Value : null;
            }
            else if (Complete.IsMatch(line)) result.Kind = ServeWebLogKind.ShutdownComplete;
            else if ((m = Fatal.Match(line)).Success) result.Kind = FatalKind(m.Groups[1].Value);
            return result;
        }

        private static ServeWebLogKind FatalKind(string message)
        {
            if (message.IndexOf("camera or microphone is in use", StringComparison.OrdinalIgnoreCase) >= 0) return ServeWebLogKind.MediaInUse;
            if (message.StartsWith("Failed to start service", StringComparison.Ordinal)) return ServeWebLogKind.ServiceUnavailable;
            if (message.StartsWith("NoDeviceConnectedError", StringComparison.Ordinal)
                || message.StartsWith("Device is not connected", StringComparison.Ordinal)
                || message.StartsWith("Device not found", StringComparison.Ordinal)
                || message.StartsWith("DeviceNotFoundError", StringComparison.Ordinal)) return ServeWebLogKind.DeviceNotFound;
            return ServeWebLogKind.Error;
        }

        private static Regex Line(string pattern) => new Regex(pattern, RegexOptions.CultureInvariant);
    }
}

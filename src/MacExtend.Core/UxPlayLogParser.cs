using System.Text.RegularExpressions;

namespace MacExtend.Core
{
    public enum LogEventKind
    {
        None,
        ClientConnecting,
        ConnectionLost,
        Error,
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
            return LogEvent.None;
        }
    }
}

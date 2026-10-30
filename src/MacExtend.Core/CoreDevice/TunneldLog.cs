using System.Text.RegularExpressions;

namespace MacExtend.Core.CoreDevice
{
    public enum TunneldLogKind
    {
        Other,
        Listening,
        TunnelCreated,
        TunnelClosed,
        /// <summary>The device refused a QUIC tunnel (iOS 18.2+): tunneld is on Python older than 3.13.</summary>
        QuicRefused,
        Error,
    }

    /// <summary>Classifies tunneld's output for logs\tunneld.log. Lines carry the UDID and addresses, so only the kind is kept.</summary>
    public static class TunneldLog
    {
        private const string ServerLogger = @"pymobiledevice3\.tunneld\.server";

        private static readonly Regex Ansi = new Regex(@"\x1b\[[0-9;]*m", RegexOptions.CultureInvariant);
        private static readonly Regex Listening = new Regex(@"Uvicorn running on http://127\.0\.0\.1:49151", RegexOptions.CultureInvariant);
        private static readonly Regex Created = new Regex(LogPrefix.Build(ServerLogger, "INFO") + @"\[[^\]]*\] Created tunnel --rsd ", RegexOptions.CultureInvariant);
        private static readonly Regex Closed = new Regex(LogPrefix.Build(ServerLogger, "INFO") + @"Disconnected from tunnel --rsd ", RegexOptions.CultureInvariant);
        private static readonly Regex Quic = new Regex(LogPrefix.Build(ServerLogger, "WARNING") + @".*QuicProtocolNotSupportedError", RegexOptions.CultureInvariant);
        private static readonly Regex Failure = new Regex(LogPrefix.Build(ServerLogger, "ERROR"), RegexOptions.CultureInvariant);

        public static TunneldLogKind Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return TunneldLogKind.Other;
            line = Ansi.Replace(line, "");
            if (Listening.IsMatch(line)) return TunneldLogKind.Listening;
            if (Created.IsMatch(line)) return TunneldLogKind.TunnelCreated;
            if (Closed.IsMatch(line)) return TunneldLogKind.TunnelClosed;
            if (Quic.IsMatch(line)) return TunneldLogKind.QuicRefused;
            if (Failure.IsMatch(line)) return TunneldLogKind.Error;
            return TunneldLogKind.Other;
        }

        public static string Describe(TunneldLogKind kind)
        {
            switch (kind)
            {
                case TunneldLogKind.Listening: return "listening on 127.0.0.1:49151";
                case TunneldLogKind.TunnelCreated: return "tunnel created";
                case TunneldLogKind.TunnelClosed: return "tunnel closed";
                case TunneldLogKind.QuicRefused: return "device refused a QUIC tunnel (needs Python 3.13+)";
                case TunneldLogKind.Error: return "error while starting a tunnel";
                default: return "other";
            }
        }
    }
}

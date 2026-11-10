using System;
using System.Collections.Generic;

namespace MacExtend.Core.Wda
{
    /// <summary>WDA's screen size in points, in the current interface orientation.</summary>
    public struct DeviceSize : IEquatable<DeviceSize>
    {
        public DeviceSize(double width, double height)
        {
            Width = width;
            Height = height;
        }

        public double Width { get; }
        public double Height { get; }
        public bool IsKnown => Width > 0 && Height > 0;
        public bool IsLandscape => Width > Height;
        /// <summary>Long side over short side: about 2.17 on Face ID iPhones, 1.78 with a Home button, 1.33 to 1.43 on iPads.</summary>
        public double Elongation => IsKnown ? Math.Max(Width, Height) / Math.Min(Width, Height) : 0;

        public bool Equals(DeviceSize other) => Width.Equals(other.Width) && Height.Equals(other.Height);
        public override bool Equals(object obj) => obj is DeviceSize other && Equals(other);
        public override int GetHashCode() => Width.GetHashCode() * 397 ^ Height.GetHashCode();
        public override string ToString() => Width.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "x"
                                             + Height.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>What a WDA answer says. W3C WDA wraps everything in {"value": ...}; errors are {"value": {"error", "message"}}.</summary>
    public static class WdaResponses
    {
        public const string InvalidSession = "invalid session id";

        /// <summary>GET /status: WDA answers and says it is ready (a build without the flag counts as ready).</summary>
        public static bool IsReady(string json)
        {
            var value = Value(json);
            if (!(value is Dictionary<string, object> map)) return false;
            return !map.TryGetValue("ready", out var ready) || !(ready is bool b) || b;
        }

        /// <summary>POST /session: the new session's id, at the top level or inside value; null when there is none.</summary>
        public static string SessionId(string json)
        {
            if (!MiniJson.TryParse(json ?? "", out var root) || !(root is Dictionary<string, object> map)) return null;
            if (map.TryGetValue("sessionId", out var top) && top is string id && id.Length > 0) return id;
            if (map.TryGetValue("value", out var value) && value is Dictionary<string, object> inner
                && inner.TryGetValue("sessionId", out var nested) && nested is string id2 && id2.Length > 0) return id2;
            return null;
        }

        /// <summary>GET /session/:id/window/size: {"value": {"width", "height"}}; unknown (0x0) when it is not that.</summary>
        public static DeviceSize WindowSize(string json)
        {
            if (!(Value(json) is Dictionary<string, object> map)) return default(DeviceSize);
            var width = map.TryGetValue("width", out var w) && w is double dw ? dw : 0;
            var height = map.TryGetValue("height", out var h) && h is double dh ? dh : 0;
            return width > 0 && height > 0 ? new DeviceSize(width, height) : default(DeviceSize);
        }

        /// <summary>The W3C error code of a failed request ("invalid session id", "no such element", ...), or null.</summary>
        public static string Error(string json)
        {
            if (!(Value(json) is Dictionary<string, object> map)) return null;
            return map.TryGetValue("error", out var e) && e is string s && s.Length > 0 ? s : null;
        }

        /// <summary>
        /// The session in the path is gone: HTTP 404 with "invalid session id", or a bare 404 (the session part of the path
        /// was not recognised either). A pre-W3C build says the same with HTTP 200 and status 6.
        /// </summary>
        public static bool IsInvalidSession(int status, string json)
        {
            var error = Error(json);
            if (error == InvalidSession) return true;
            if (status == 404 && error == null) return true;
            return status == 200 && LegacyStatus(json) == 6;
        }

        /// <summary>A pre-W3C (JSONWP) build reports failures as HTTP 200 with a non-zero "status".</summary>
        public static bool IsLegacyFailure(int status, string json) => status == 200 && LegacyStatus(json) is int s && s != 0;

        private static int? LegacyStatus(string json)
        {
            if (!MiniJson.TryParse(json ?? "", out var root) || !(root is Dictionary<string, object> map)) return null;
            return map.TryGetValue("status", out var s) && s is double d && d == Math.Floor(d) ? (int)d : (int?)null;
        }

        private static object Value(string json)
        {
            if (!MiniJson.TryParse(json ?? "", out var root) || !(root is Dictionary<string, object> map)) return null;
            return map.TryGetValue("value", out var value) ? value : null;
        }
    }
}

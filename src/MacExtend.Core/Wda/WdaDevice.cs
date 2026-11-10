using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Wda
{
    /// <summary>The WDA session as the UI shows it. Immutable.</summary>
    public sealed class WdaView
    {
        public static readonly WdaView Off = new WdaView(WdaPhase.Off, WdaStep.FindingDevice, WdaFailure.None, null, null);

        public WdaView(WdaPhase phase, WdaStep step, WdaFailure failure, string udid, DateTime? expiresUtc)
        {
            Phase = phase;
            Step = step;
            Failure = failure;
            Udid = udid;
            ExpiresUtc = expiresUtc;
        }

        public WdaPhase Phase { get; }
        public WdaStep Step { get; }
        public WdaFailure Failure { get; }
        /// <summary>The device of the session; null while none is chosen.</summary>
        public string Udid { get; }
        /// <summary>When the provisioning profile WDA was signed with expires, if the device said.</summary>
        public DateTime? ExpiresUtc { get; }
        public bool IsReady => Phase == WdaPhase.Ready;
    }

    /// <summary>Picks WebDriverAgent out of `apps list --type User`: {"&lt;bundle id&gt;": {CFBundleName, CFBundleExecutable, ...}}.</summary>
    public static class WdaBundles
    {
        /// <summary>
        /// The runner's bundle id, or null. Re-signing tools may rename the bundle id, so the name and the executable count
        /// too; a ".xctrunner" bundle (the runner itself) comes before anything else that mentions WebDriverAgent.
        /// </summary>
        public static string Pick(string appsJson)
        {
            if (!MiniJson.TryParse((appsJson ?? "").Trim(), out var value)) return null;
            var apps = new List<KeyValuePair<string, Dictionary<string, object>>>();
            if (value is Dictionary<string, object> map)
            {
                foreach (var pair in map) apps.Add(new KeyValuePair<string, Dictionary<string, object>>(pair.Key, pair.Value as Dictionary<string, object>));
            }
            else if (value is List<object> list)
            {
                foreach (var info in list.OfType<Dictionary<string, object>>())
                {
                    if (info.TryGetValue("CFBundleIdentifier", out var id) && id is string s) apps.Add(new KeyValuePair<string, Dictionary<string, object>>(s, info));
                }
            }
            return apps.Where(a => IsBundleId(a.Key) && IsWda(a.Key, a.Value))
                       .OrderBy(a => a.Key.EndsWith(".xctrunner", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                       .ThenBy(a => a.Key.Length)
                       .ThenBy(a => a.Key, StringComparer.Ordinal)
                       .Select(a => a.Key)
                       .FirstOrDefault();
        }

        /// <summary>Letters, digits, '.', '-' and '_' only, starting with a letter or digit: it goes on a command line, never as an option.</summary>
        public static bool IsBundleId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length <= 255 && char.IsLetterOrDigit(id[0]) && id[0] < 0x80
            && id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_');

        private static bool IsWda(string id, Dictionary<string, object> info)
        {
            if (Mentions(id)) return true;
            if (info == null) return false;
            return new[] { "CFBundleName", "CFBundleExecutable", "CFBundleDisplayName" }.Any(k => info.TryGetValue(k, out var v) && v is string s && Mentions(s));
        }

        private static bool Mentions(string s) => s.IndexOf("WebDriverAgent", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>When WDA's signature runs out, from `provision list`: one plist per profile, ExpirationDate in ISO 8601 (UTC).</summary>
    public static class ProvisionProfiles
    {
        /// <summary>Under this much left, the user is told to sign WDA again.</summary>
        public static readonly TimeSpan WarnBefore = TimeSpan.FromHours(24);

        /// <summary>
        /// The latest expiry among the profiles whose application-identifier ("TEAMID.bundle" or "TEAMID.prefix*") covers
        /// <paramref name="bundleId"/>; null when none does or the output is not a profile list.
        /// </summary>
        public static DateTime? Expiry(string profilesJson, string bundleId)
        {
            if (string.IsNullOrEmpty(bundleId) || !MiniJson.TryParse((profilesJson ?? "").Trim(), out var value) || !(value is List<object> list)) return null;
            DateTime? best = null;
            foreach (var profile in list.OfType<Dictionary<string, object>>())
            {
                if (!Covers(profile, bundleId)) continue;
                if (!profile.TryGetValue("ExpirationDate", out var date) || !(date is string text) || !TryParseUtc(text, out var expires)) continue;
                if (best == null || expires > best.Value) best = expires;
            }
            return best;
        }

        public static bool IsExpired(DateTime? expiresUtc, DateTime nowUtc) => expiresUtc != null && expiresUtc.Value <= nowUtc;

        public static bool ExpiresSoon(DateTime? expiresUtc, DateTime nowUtc) => expiresUtc != null && expiresUtc.Value - nowUtc < WarnBefore;

        private static bool Covers(Dictionary<string, object> profile, string bundleId)
        {
            if (!profile.TryGetValue("Entitlements", out var e) || !(e is Dictionary<string, object> entitlements)) return false;
            if (!entitlements.TryGetValue("application-identifier", out var a) || !(a is string appId)) return false;
            var dot = appId.IndexOf('.');
            if (dot <= 0) return false;
            var pattern = appId.Substring(dot + 1);
            if (pattern.EndsWith("*", StringComparison.Ordinal)) return bundleId.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal);
            return string.Equals(pattern, bundleId, StringComparison.Ordinal);
        }

        private static bool TryParseUtc(string text, out DateTime utc)
        {
            // plistlib gives naive UTC datetimes, which isoformat() writes without an offset; an offset, if any, is honoured
            var styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, styles, out utc);
        }
    }

    /// <summary>The XCUITest runner's command line, with the interpreter flags every runtime command uses.</summary>
    public static class WdaArguments
    {
        /// <summary>
        /// The XCUITest runner that starts WDA and keeps it alive, over the cable's in-process userspace tunnel (iOS 17.4+,
        /// no administrator). The device comes from PYMOBILEDEVICE3_UDID, like serve-web's.
        /// </summary>
        public static IReadOnlyList<string> XcuiTest(string bundleId)
        {
            if (!WdaBundles.IsBundleId(bundleId)) throw new ArgumentException("not a bundle id", nameof(bundleId));
            return CoreDeviceArguments.Command("developer", "dvt", "xcuitest", bundleId, "--userspace");
        }
    }

    /// <summary>
    /// When WDA runs: the switch is on and an iPhone or iPad mirrors. It stays up for <see cref="Linger"/> after mirroring
    /// ends, so a session that drops and comes straight back does not launch the runner again.
    /// </summary>
    public sealed class WdaLifetime
    {
        public static readonly TimeSpan Linger = TimeSpan.FromSeconds(10);

        private readonly Func<DateTime> _clock;
        private DateTime? _ended;
        private bool _running;

        public WdaLifetime(Func<DateTime> clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>uxplay's "connection request from NAME (MODEL)": iPhone and iPad models only, never a Mac.</summary>
        public static bool IsMobileModel(string model) =>
            model != null && (model.StartsWith("iPhone", StringComparison.Ordinal) || model.StartsWith("iPad", StringComparison.Ordinal));

        public bool ShouldRun(bool enabled, bool mobileMirroring)
        {
            if (!enabled)
            {
                _running = false;
                _ended = null;
            }
            else if (mobileMirroring)
            {
                _running = true;
                _ended = null;
            }
            else if (_running)
            {
                var now = _clock();
                if (_ended == null) _ended = now;
                if (now - _ended.Value >= Linger)
                {
                    _running = false;
                    _ended = null;
                }
            }
            return _running;
        }
    }

    public enum WdaTargetKind
    {
        /// <summary>No device on the cable.</summary>
        None,
        Use,
        /// <summary>Several devices and none remembered for the one mirroring: the user picks.</summary>
        Pick,
    }

    /// <summary>Which cable device a session controls.</summary>
    public sealed class WdaTarget
    {
        private WdaTarget(WdaTargetKind kind, string udid, IReadOnlyList<string> choices)
        {
            Kind = kind;
            Udid = udid;
            Choices = choices;
        }

        public WdaTargetKind Kind { get; }
        public string Udid { get; }
        public IReadOnlyList<string> Choices { get; }

        /// <param name="usb">The devices on the cable.</param>
        /// <param name="remembered">The device picked before for the AirPlay name now mirroring, or null.</param>
        public static WdaTarget Choose(IReadOnlyList<string> usb, string remembered)
        {
            var devices = (usb ?? new string[0]).Where(CoreDeviceOptions.IsValidUdid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (devices.Count == 0) return new WdaTarget(WdaTargetKind.None, null, devices);
            if (devices.Count == 1) return new WdaTarget(WdaTargetKind.Use, devices[0], devices);
            var match = devices.FirstOrDefault(d => string.Equals(d, remembered, StringComparison.OrdinalIgnoreCase));
            return match != null ? new WdaTarget(WdaTargetKind.Use, match, devices) : new WdaTarget(WdaTargetKind.Pick, null, devices);
        }
    }

    /// <summary>
    /// The device picked for each AirPlay name, kept in the settings as "name=UDID" pairs joined by '|'. A name may hold
    /// any character but those two and line breaks, which are dropped.
    /// </summary>
    public static class WdaChoices
    {
        public const int MaxEntries = 16;

        public static IReadOnlyDictionary<string, string> Parse(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in (text ?? "").Split('|'))
            {
                var eq = entry.LastIndexOf('=');
                if (eq <= 0) continue;
                var name = entry.Substring(0, eq);
                var udid = entry.Substring(eq + 1);
                if (CoreDeviceOptions.IsValidUdid(udid)) map[name] = udid;
            }
            return map;
        }

        public static string Remembered(string text, string airPlayName) =>
            airPlayName != null && Parse(text).TryGetValue(Clean(airPlayName), out var udid) ? udid : null;

        /// <summary>The setting with <paramref name="airPlayName"/> mapped to <paramref name="udid"/>, newest last, at most <see cref="MaxEntries"/>.</summary>
        public static string Remember(string text, string airPlayName, string udid)
        {
            if (string.IsNullOrEmpty(airPlayName) || !CoreDeviceOptions.IsValidUdid(udid)) return text ?? "";
            var name = Clean(airPlayName);
            if (name.Length == 0) return text ?? "";
            var entries = Parse(text).Where(p => p.Key != name).Select(p => p.Key + "=" + p.Value).ToList();
            entries.Add(name + "=" + udid);
            return string.Join("|", entries.Skip(Math.Max(0, entries.Count - MaxEntries)));
        }

        private static string Clean(string name) => new string(name.Where(c => c != '|' && c != '=' && c != '\r' && c != '\n').ToArray()).Trim();
    }
}

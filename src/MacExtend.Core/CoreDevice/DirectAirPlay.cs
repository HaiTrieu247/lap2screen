using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>Where "Điều khiển trực tiếp" is: what the settings page and the tray show.</summary>
    public enum DirectPhase
    {
        /// <summary>The switch is off.</summary>
        Off,
        /// <summary>On, waiting for an iPhone or iPad to mirror.</summary>
        Waiting,
        /// <summary>Looking at the devices on the cable and on Wi-Fi.</summary>
        Finding,
        /// <summary>Several devices and none known for the one mirroring: the user picks.</summary>
        Choose,
        /// <summary>A session runs; its own phase says how far it is.</summary>
        Running,
        Failed,
    }

    /// <summary>The switch's state as the UI shows it. Immutable.</summary>
    public sealed class DirectView
    {
        public static readonly DirectView Off = new DirectView(DirectPhase.Off, SessionFailure.None);

        private DirectView(DirectPhase phase, SessionFailure failure)
        {
            Phase = phase;
            Failure = failure;
        }

        public static DirectView Of(DirectPhase phase) =>
            phase == DirectPhase.Failed ? Failed(SessionFailure.Other) : new DirectView(phase, SessionFailure.None);

        public static DirectView Failed(SessionFailure failure) => new DirectView(DirectPhase.Failed, failure);

        public DirectPhase Phase { get; }
        /// <summary>None unless <see cref="Phase"/> is Failed.</summary>
        public SessionFailure Failure { get; }
    }

    /// <summary>
    /// When a session runs: the switch is on and an iPhone or iPad mirrors. It stays up for <see cref="Linger"/> after
    /// mirroring ends, so a mirroring session that drops and comes straight back does not start serve-web again.
    /// </summary>
    public sealed class DirectLifetime
    {
        public static readonly TimeSpan Linger = TimeSpan.FromSeconds(10);

        private readonly Func<DateTime> _clock;
        private DateTime? _ended;
        private bool _running;

        public DirectLifetime(Func<DateTime> clock)
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

    /// <summary>
    /// The device picked for each AirPlay name, kept in the settings as "name=UDID" pairs joined by '|'. A name may hold
    /// any character but those two and line breaks, which are dropped.
    /// </summary>
    public static class RememberedDevices
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

    /// <summary>A device on the cable with what lockdown says about it; Name and Model are null when it did not say.</summary>
    public sealed class UsbDevice
    {
        public UsbDevice(string udid, string name, string model)
        {
            Udid = udid ?? throw new ArgumentNullException(nameof(udid));
            Name = name;
            Model = model;
        }

        public string Udid { get; }
        /// <summary>The name set on the device, which is also its AirPlay name unless the user changed one of them.</summary>
        public string Name { get; }
        /// <summary>ProductType, the same model string AirPlay sends ("iPhone12,1").</summary>
        public string Model { get; }
    }

    /// <summary>Which device the session for the device mirroring now controls, when the plan has several to offer.</summary>
    public static class DirectTarget
    {
        /// <summary>
        /// A plan that asks to pick is settled, in this order, by the device picked before for this AirPlay name, the
        /// only candidate on the cable named like it, or the only one of its model. Otherwise the user still picks.
        /// </summary>
        /// <param name="named">What the cable devices say about themselves; null when that was not asked.</param>
        public static DirectOpenPlan Resolve(DirectOpenPlan plan, string remembered, IReadOnlyList<UsbDevice> named, string airPlayName, string airPlayModel)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.Step != DirectOpenStep.Pick) return plan;
            var choice = plan.Choices.FirstOrDefault(c => string.Equals(c.Udid, remembered, StringComparison.OrdinalIgnoreCase))
                         ?? Only(plan, named, d => SameName(d.Name, airPlayName))
                         ?? Only(plan, named, d => !string.IsNullOrEmpty(airPlayModel) && string.Equals(d.Model, airPlayModel, StringComparison.Ordinal));
            return choice == null ? plan : DirectOpenPlan.Open(choice.Udid, choice.Route);
        }

        private static DeviceChoice Only(DirectOpenPlan plan, IReadOnlyList<UsbDevice> named, Func<UsbDevice, bool> match)
        {
            if (named == null) return null;
            var matches = plan.Choices.Where(c => named.Any(d => string.Equals(d.Udid, c.Udid, StringComparison.OrdinalIgnoreCase) && match(d))).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        /// <summary>Case aside, and whether the accents are composed or not (the device and AirPlay may send either).</summary>
        private static bool SameName(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
            && string.Equals(a.Trim().Normalize(), b.Trim().Normalize(), StringComparison.OrdinalIgnoreCase);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MacExtend.Core.Wda
{
    public enum TouchKind
    {
        Move,
        Down,
        Up,
        Pause,
    }

    /// <summary>One step of a W3C touch pointer action: where, in WDA points of the current orientation, and for how long.</summary>
    public struct TouchStep : IEquatable<TouchStep>
    {
        private TouchStep(TouchKind kind, int x, int y, int durationMs)
        {
            Kind = kind;
            X = x;
            Y = y;
            DurationMs = durationMs;
        }

        public TouchKind Kind { get; }
        public int X { get; }
        public int Y { get; }
        public int DurationMs { get; }

        public static TouchStep Move(double x, double y, int durationMs) =>
            new TouchStep(TouchKind.Move, (int)Math.Round(x), (int)Math.Round(y), Math.Max(0, durationMs));

        public static TouchStep Down() => new TouchStep(TouchKind.Down, 0, 0, 0);
        public static TouchStep Up() => new TouchStep(TouchKind.Up, 0, 0, 0);
        public static TouchStep Pause(int durationMs) => new TouchStep(TouchKind.Pause, 0, 0, Math.Max(0, durationMs));

        public bool Equals(TouchStep other) => Kind == other.Kind && X == other.X && Y == other.Y && DurationMs == other.DurationMs;
        public override bool Equals(object obj) => obj is TouchStep other && Equals(other);
        public override int GetHashCode() => (((int)Kind * 397 ^ X) * 397 ^ Y) * 397 ^ DurationMs;

        public override string ToString() =>
            Kind == TouchKind.Move ? "move(" + X + "," + Y + "," + DurationMs + ")" : Kind == TouchKind.Pause ? "pause(" + DurationMs + ")" : Kind.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// The JSON bodies the app sends to WebDriverAgent (Appium's W3C WebDriver server on the device, port 8100).
    /// Checked against appium/WebDriverAgent v16.15.3: POST /session, /session/:id/appium/settings, /session/:id/actions,
    /// /session/:id/wda/keys, /session/:id/wda/pressButton, /session/:id/wda/performIoHidEvent and /wda/homescreen.
    /// </summary>
    public static class WdaRequests
    {
        public const string Empty = "{}";

        /// <summary>HID usage of the Home button (consumer page, "Menu"), for performIoHidEvent.</summary>
        public const int HidPageConsumer = 0x0C;
        public const int HidUsageHome = 0x40;

        /// <summary>A session that never waits for the app to go idle or for animations, so a touch goes out at once.</summary>
        public static string NewSession()
        {
            var always = Obj(("shouldWaitForQuiescence", (object)false), ("waitForIdleTimeout", 0));
            return MiniJson.Write(Obj(("capabilities", (object)Obj(("alwaysMatch", (object)always)))));
        }

        public static string Settings() => MiniJson.Write(Obj(
            ("settings", Obj(("waitForIdleTimeout", (object)0), ("animationCoolOffTimeout", 0), ("snapshotTimeout", 1)))));

        /// <summary>One touch pointer ("finger1") running <paramref name="steps"/> in order.</summary>
        public static string Actions(IReadOnlyList<TouchStep> steps)
        {
            if (steps == null || steps.Count == 0) throw new ArgumentException("no touch steps", nameof(steps));
            var actions = steps.Select(Step).ToList();
            var pointer = Obj(("type", (object)"pointer"), ("id", "finger1"), ("parameters", Obj(("pointerType", (object)"touch"))), ("actions", actions));
            return MiniJson.Write(Obj(("actions", (object)new List<object> { pointer })));
        }

        /// <summary>
        /// Text for the focused field, one string per character as the user sees it (a surrogate pair or a letter with its
        /// combining marks stays whole). XCTest types "\b" as Delete and "\n" as Return.
        /// </summary>
        public static string Keys(string text)
        {
            var value = new List<object>();
            var e = StringInfo.GetTextElementEnumerator(text ?? "");
            while (e.MoveNext()) value.Add(e.GetTextElement());
            return MiniJson.Write(Obj(("value", (object)value)));
        }

        /// <summary>home, volumeUp or volumeDown.</summary>
        public static string Button(string name) => MiniJson.Write(Obj(("name", (object)(name ?? throw new ArgumentNullException(nameof(name))))));

        /// <summary>A short press of the Home button as a HID event (a double press needs two of these back to back).</summary>
        public static string HidHome() => MiniJson.Write(Obj(("page", (object)HidPageConsumer), ("usage", HidUsageHome), ("duration", 0.005)));

        private static object Step(TouchStep step)
        {
            switch (step.Kind)
            {
                case TouchKind.Move:
                    return Obj(("type", (object)"pointerMove"), ("duration", step.DurationMs), ("x", step.X), ("y", step.Y));
                case TouchKind.Down:
                    return Obj(("type", (object)"pointerDown"), ("button", 0));
                case TouchKind.Up:
                    return Obj(("type", (object)"pointerUp"), ("button", 0));
                default:
                    return Obj(("type", (object)"pause"), ("duration", step.DurationMs));
            }
        }

        private static List<KeyValuePair<string, object>> Obj(params (string Key, object Value)[] pairs) =>
            pairs.Select(p => new KeyValuePair<string, object>(p.Key, p.Value)).ToList();
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MacExtend.Core
{
    /// <summary>Reads and writes <see cref="AppSettings"/> as "Key=Value" lines. Parsing never throws.</summary>
    public static class SettingsStore
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static AppSettings Parse(string text)
        {
            var s = new AppSettings();
            foreach (var pair in ReadPairs(text ?? ""))
            {
                Apply(s, pair.Key, pair.Value);
            }
            s.Clamp();
            return s;
        }

        public static string Serialize(AppSettings s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# MacExtend settings");
            Line(sb, "DeviceName", s.DeviceName);
            Line(sb, "ResolutionMode", s.ResolutionMode.ToString());
            Line(sb, "CustomWidth", s.CustomWidth.ToString(CultureInfo.InvariantCulture));
            Line(sb, "CustomHeight", s.CustomHeight.ToString(CultureInfo.InvariantCulture));
            Line(sb, "CustomRefresh", s.CustomRefresh.ToString(CultureInfo.InvariantCulture));
            Line(sb, "CustomH265", Bool(s.CustomH265));
            Line(sb, "Fps", s.Fps.ToString(CultureInfo.InvariantCulture));
            Line(sb, "AudioEnabled", Bool(s.AudioEnabled));
            Line(sb, "PinEnabled", Bool(s.PinEnabled));
            Line(sb, "Pin", s.Pin);
            Line(sb, "StartWithWindows", Bool(s.StartWithWindows));
            Line(sb, "AutoCheckUpdates", Bool(s.AutoCheckUpdates));
            Line(sb, "FocusOnConnect", Bool(s.FocusOnConnect));
            Line(sb, "HideCursor", Bool(s.HideCursor));
            Line(sb, "Fullscreen", Bool(s.Fullscreen));
            Line(sb, "AlwaysOnTop", Bool(s.AlwaysOnTop));
            Line(sb, "BluetoothControl", Bool(s.BluetoothControl));
            Line(sb, "DirectWifiDevices", s.DirectWifiDevices);
            Line(sb, "DirectLastDevice", s.DirectLastDevice);
            Line(sb, "DirectHintDismissed", Bool(s.DirectHintDismissed));
            Line(sb, "DirectWindowBounds", s.DirectWindowBounds);
            Line(sb, "DirectOnTop", Bool(s.DirectOnTop));
            return sb.ToString();
        }

        public static AppSettings Load(string path)
        {
            try
            {
                return File.Exists(path) ? Parse(File.ReadAllText(path, Utf8NoBom)) : Parse("");
            }
            catch (IOException)
            {
                return Parse("");
            }
            catch (UnauthorizedAccessException)
            {
                return Parse("");
            }
        }

        /// <summary>Writes to a temporary file first so a crash never leaves a half-written settings file.</summary>
        public static void Save(string path, AppSettings s)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize(s), Utf8NoBom);
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> ReadPairs(string text)
        {
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                yield return new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
            }
        }

        private static void Apply(AppSettings s, string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "devicename": s.DeviceName = value; break;
                case "resolutionmode":
                    if (Enum.TryParse(value, true, out ResolutionMode mode) && Enum.IsDefined(typeof(ResolutionMode), mode)) s.ResolutionMode = mode;
                    break;
                case "customwidth": if (TryInt(value, out var w)) s.CustomWidth = w; break;
                case "customheight": if (TryInt(value, out var h)) s.CustomHeight = h; break;
                case "customrefresh": if (TryInt(value, out var r)) s.CustomRefresh = r; break;
                case "customh265": if (TryBool(value, out var h265)) s.CustomH265 = h265; break;
                case "fps": if (TryInt(value, out var fps)) s.Fps = fps; break;
                case "audioenabled": if (TryBool(value, out var audio)) s.AudioEnabled = audio; break;
                case "pinenabled": if (TryBool(value, out var pinOn)) s.PinEnabled = pinOn; break;
                case "pin": s.Pin = value; break;
                case "startwithwindows": if (TryBool(value, out var auto)) s.StartWithWindows = auto; break;
                case "autocheckupdates": if (TryBool(value, out var check)) s.AutoCheckUpdates = check; break;
                case "focusonconnect": if (TryBool(value, out var focus)) s.FocusOnConnect = focus; break;
                case "hidecursor": if (TryBool(value, out var hide)) s.HideCursor = hide; break;
                case "fullscreen": if (TryBool(value, out var fullscreen)) s.Fullscreen = fullscreen; break;
                case "alwaysontop": if (TryBool(value, out var onTop)) s.AlwaysOnTop = onTop; break;
                case "bluetoothcontrol": if (TryBool(value, out var bluetooth)) s.BluetoothControl = bluetooth; break;
                case "directwifidevices": s.DirectWifiDevices = value; break;
                case "directlastdevice": s.DirectLastDevice = value; break;
                case "directhintdismissed": if (TryBool(value, out var hint)) s.DirectHintDismissed = hint; break;
                case "directwindowbounds": s.DirectWindowBounds = value; break;
                case "directontop": if (TryBool(value, out var directOnTop)) s.DirectOnTop = directOnTop; break;
            }
        }

        private static bool TryInt(string value, out int result) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

        private static bool TryBool(string value, out bool result) => bool.TryParse(value, out result);

        private static string Bool(bool b) => b ? "true" : "false";

        private static void Line(StringBuilder sb, string key, string value) =>
            sb.Append(key).Append('=').Append((value ?? "").Replace("\r", "").Replace("\n", " ")).Append('\n');
    }
}

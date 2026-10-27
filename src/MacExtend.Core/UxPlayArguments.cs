using System;
using System.Collections.Generic;
using System.Globalization;

namespace MacExtend.Core
{
    public sealed class UxPlayPaths
    {
        public UxPlayPaths(string keyFile, string registerFile)
        {
            KeyFile = keyFile;
            RegisterFile = registerFile;
        }

        /// <summary>Persistent server key, so the Mac keeps recognising this receiver.</summary>
        public string KeyFile { get; }
        /// <summary>Register of Macs that already entered the PIN.</summary>
        public string RegisterFile { get; }
    }

    /// <summary>uxplay command line tuned for "Use as Separate Display" from a Mac.</summary>
    public static class UxPlayArguments
    {
        /// <param name="pathResolver">Maps a path to one uxplay can open (8.3 short path for non-ASCII paths on Windows).</param>
        public static IReadOnlyList<string> Build(AppSettings s, string deviceName, RequestedVideo video,
                                                  UxPlayPaths paths, Func<string, string> pathResolver)
        {
            var args = new List<string>
            {
                "-n", deviceName, "-nh",
                "-s", video.Mode.ToString(),
                "-fps", s.Fps.ToString(CultureInfo.InvariantCulture),
                // no audio/video timestamp sync: lowest latency, recommended by UxPlay for second-monitor use
                "-vsync", "no",
                "-vs", "d3d11videosink", "-fs",
                // a reconnecting Mac replaces a stale session instead of being refused
                "-nohold",
                "-key", pathResolver(paths.KeyFile),
            };

            if (s.PinEnabled)
            {
                if (!Pin.IsValid(s.Pin)) throw new ArgumentException("PIN is enabled but not a valid 4-digit PIN.", nameof(s));
                args.Add("-pin");
                args.Add(s.Pin);
                args.Add("-reg");
                args.Add(pathResolver(paths.RegisterFile));
            }

            if (!s.AudioEnabled)
            {
                args.Add("-as");
                args.Add("0");
            }

            if (video.H265) args.Add("-h265");

            return args;
        }
    }
}

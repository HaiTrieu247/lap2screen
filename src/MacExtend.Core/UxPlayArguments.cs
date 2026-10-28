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
        /// <summary>
        /// Plain sink: uxplay itself adds Alt+Enter toggling and opens a normal window that can be moved
        /// and resized. "-fs" is never passed: with d3d11videosink it starts full screen with Alt+Enter turned off.
        /// </summary>
        public const string WindowedSink = "d3d11videosink";

        /// <summary>
        /// Starts full screen and keeps Alt+Enter. uxplay splits the "-vs" value at the first space into the
        /// sink and its properties, so this stays one argument.
        /// </summary>
        public const string FullscreenSink = "d3d11videosink fullscreen-toggle-mode=alt-enter+property fullscreen=true";

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
                "-vs", s.Fullscreen ? FullscreenSink : WindowedSink,
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

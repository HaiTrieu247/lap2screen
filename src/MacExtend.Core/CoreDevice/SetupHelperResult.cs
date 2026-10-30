using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// The result file of the elevated "MacExtend.exe --coredevice-setup" helper: the outcome on the first line ("ok" or
    /// "failed: ..."), then the helper's step lines. The unelevated app writes those lines to logs\coredevice.log, so
    /// the elevated process writes nothing into the user's folders except this one new file.
    /// </summary>
    public sealed class SetupHelperResult
    {
        public const string Ok = "ok";

        private SetupHelperResult(string outcome, IReadOnlyList<string> lines)
        {
            Outcome = outcome;
            Lines = lines;
        }

        /// <summary>"ok" or "failed: ..."; null when the file was empty.</summary>
        public string Outcome { get; }
        public IReadOnlyList<string> Lines { get; }
        public bool Succeeded => Outcome == Ok;

        public static string Format(string outcome, IEnumerable<string> lines) =>
            string.Concat(new[] { outcome }.Concat(lines ?? Enumerable.Empty<string>()).Select(line => OneLine(line) + "\n"));

        public static SetupHelperResult Parse(string text)
        {
            var lines = (text ?? "").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
            return lines.Count == 0
                ? new SetupHelperResult(null, new string[0])
                : new SetupHelperResult(lines[0], lines.Skip(1).ToList());
        }

        // an exception message can span lines; one record stays one line
        private static string OneLine(string text) => (text ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}

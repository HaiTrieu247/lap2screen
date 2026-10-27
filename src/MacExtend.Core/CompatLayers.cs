using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core
{
    /// <summary>
    /// Windows compatibility layers ("AppCompatFlags\Layers" registry values) for uxplay.exe.
    /// uxplay and GStreamer's d3d11 window do not declare DPI awareness, so at 125 % or 150 %
    /// scaling Windows renders the full-screen video at a lower size and stretches it: the Mac's
    /// desktop looks blurry. HIGHDPIAWARE is the "Override high DPI scaling: Application" setting.
    /// </summary>
    public static class CompatLayers
    {
        private const string HighDpiAware = "HIGHDPIAWARE";
        private static readonly string[] ConflictingDpiFlags = { "DPIUNAWARE", "GDIDPISCALING" };

        public static string WithHighDpiAware(string existing)
        {
            var flags = new List<string>();
            foreach (var token in (existing ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == "~" || ConflictingDpiFlags.Contains(token, StringComparer.OrdinalIgnoreCase)) continue;
                if (!flags.Contains(token, StringComparer.OrdinalIgnoreCase)) flags.Add(token);
            }
            if (!flags.Contains(HighDpiAware, StringComparer.OrdinalIgnoreCase)) flags.Add(HighDpiAware);
            return "~ " + string.Join(" ", flags);
        }
    }
}

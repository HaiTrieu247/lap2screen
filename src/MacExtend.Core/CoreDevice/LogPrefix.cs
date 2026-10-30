namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// The prefix pymobiledevice3's ansilogs puts on every line: "&lt;date&gt; &lt;time&gt; &lt;host&gt; &lt;logger&gt;[&lt;pid&gt;] &lt;LEVEL&gt; ".
    /// One builder for ServeWebLog and CliLog.
    /// </summary>
    internal static class LogPrefix
    {
        /// <summary>The CLI logger: "__main__" run as `python -m pymobiledevice3`, "pymobiledevice3.__main__" imported.</summary>
        public const string Main = @"(?:pymobiledevice3\.)?__main__";

        /// <summary>A regex fragment, anchored at the line start, ending after the trailing space of the level. <paramref name="logger"/> and <paramref name="level"/> are regex fragments.</summary>
        public static string Build(string logger, string level) => @"\A\S+ \S+ \S+ " + logger + @"\[\d+\] " + level + " ";
    }
}

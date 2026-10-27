using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MacExtend.Core
{
    /// <summary>
    /// Builds a command line that the MSVCRT / CommandLineToArgvW parser splits back into the same argv.
    /// .NET Framework's ProcessStartInfo has no ArgumentList, so this is done by hand.
    /// </summary>
    public static class WindowsCommandLine
    {
        public static string Quote(string arg)
        {
            arg = arg ?? "";
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;

            var sb = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    // backslashes before a quote are escaped, then the quote itself
                    sb.Append('\\', backslashes * 2 + 1);
                }
                else
                {
                    sb.Append('\\', backslashes);
                }
                backslashes = 0;
                sb.Append(c);
            }
            // trailing backslashes precede the closing quote, so they are doubled
            sb.Append('\\', backslashes * 2);
            return sb.Append('"').ToString();
        }

        public static string Join(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));
    }
}

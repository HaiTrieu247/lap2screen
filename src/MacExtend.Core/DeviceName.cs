using System.Text;

namespace MacExtend.Core
{
    /// <summary>
    /// The AirPlay name passed to uxplay. uxplay on Windows receives argv in the ANSI code page,
    /// so the name is folded to plain ASCII ("Máy của Thịnh" becomes "May cua Thinh").
    /// </summary>
    public static class DeviceName
    {
        public const int MaxLength = 40;
        private const string ProductName = "MacExtend";

        public static string Sanitize(string input, string fallback)
        {
            var name = Fold(input);
            if (name.Length > 0) return name;
            name = Fold(fallback);
            return name.Length > 0 ? name : ProductName;
        }

        public static string DefaultFor(string machineName) =>
            Sanitize(ProductName + "-" + Fold(machineName), ProductName);

        private static string Fold(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var decomposed = input.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            var lastWasSpace = true;
            foreach (var c in decomposed)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace) sb.Append(' ');
                    lastWasSpace = true;
                }
                else if (IsAllowed(c))
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
                // everything else, including the accents FormD detached from their letters, is dropped
            }
            var result = sb.ToString().Trim();
            if (result.Length > MaxLength) result = result.Substring(0, MaxLength).TrimEnd();
            return result;
        }

        private static bool IsAllowed(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
            c == '.' || c == '_' || c == '\'' || c == '-' || c == '(' || c == ')';
    }
}

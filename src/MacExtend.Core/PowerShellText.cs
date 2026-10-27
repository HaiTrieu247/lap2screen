namespace MacExtend.Core
{
    public static class PowerShellText
    {
        /// <summary>
        /// A single-quoted PowerShell string literal. PowerShell treats the typographic quotes
        /// U+2018, U+2019, U+201A and U+201B as single quotes too, so each is doubled like '.
        /// The scripts using this run elevated; a folder name must not be able to end the literal.
        /// </summary>
        public static string Literal(string value)
        {
            var escaped = (value ?? "")
                .Replace("'", "''")
                .Replace("‘", "‘‘")
                .Replace("’", "’’")
                .Replace("‚", "‚‚")
                .Replace("‛", "‛‛");
            return "'" + escaped + "'";
        }
    }
}

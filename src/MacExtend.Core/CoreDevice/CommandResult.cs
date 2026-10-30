namespace MacExtend.Core.CoreDevice
{
    /// <summary>A short pymobiledevice3 command's outcome. stderr is never kept: only the kind of error it reported.</summary>
    public sealed class CommandResult
    {
        public CommandResult(int exitCode, string stdout, CliError error, bool timedOut)
        {
            ExitCode = exitCode;
            Stdout = stdout ?? "";
            Error = error;
            TimedOut = timedOut;
        }

        /// <summary>-1 when the command was stopped at its timeout.</summary>
        public int ExitCode { get; }

        public string Stdout { get; }

        public CliError Error { get; }

        public bool TimedOut { get; }

        public bool Succeeded => ExitCode == 0 && !TimedOut;
    }
}

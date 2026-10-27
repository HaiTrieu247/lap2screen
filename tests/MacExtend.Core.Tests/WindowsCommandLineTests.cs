namespace MacExtend.Core.Tests;

public class WindowsCommandLineTests
{
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("", "\"\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("C:\\x y\\", "\"C:\\x y\\\\\"")]
    [InlineData("a\\\\b", "a\\\\b")]
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
    [InlineData("tab\there", "\"tab\there\"")]
    public void QuoteFollowsCommandLineToArgvRules(string arg, string expected)
    {
        Assert.Equal(expected, WindowsCommandLine.Quote(arg));
    }

    [Fact]
    public void QuoteTreatsNullAsEmpty()
    {
        Assert.Equal("\"\"", WindowsCommandLine.Quote(null));
    }

    [Fact]
    public void JoinSeparatesQuotedArgsWithSpaces()
    {
        Assert.Equal("-n \"May cua Thinh\" -nh", WindowsCommandLine.Join(new[] { "-n", "May cua Thinh", "-nh" }));
    }
}

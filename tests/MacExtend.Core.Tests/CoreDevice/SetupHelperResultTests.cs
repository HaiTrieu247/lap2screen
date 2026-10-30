using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class SetupHelperResultTests
{
    [Fact]
    public void CarriesTheOutcomeAndTheStepLines()
    {
        var text = SetupHelperResult.Format("ok", new[] { "firewall rules: exit 0", "scheduled task: exit 0" });

        var result = SetupHelperResult.Parse(text);

        Assert.Equal("ok", result.Outcome);
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "firewall rules: exit 0", "scheduled task: exit 0" }, result.Lines);
    }

    [Fact]
    public void TheOutcomeIsTheFirstLineForScriptsThatReadTheFile()
    {
        var text = SetupHelperResult.Format("ok", new[] { "runtime cd-1 installed" });

        Assert.Equal("ok", text.Split('\n')[0]);
    }

    [Fact]
    public void KeepsAMultiLineMessageOnOneLine()
    {
        var text = SetupHelperResult.Format("failed: first\r\nsecond", new[] { "a\nb" });

        var result = SetupHelperResult.Parse(text);

        Assert.Equal("failed: first second", result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(new[] { "a b" }, result.Lines);
    }

    [Fact]
    public void ReadsAFileWithWindowsLineEnds()
    {
        var result = SetupHelperResult.Parse("ok\r\nruntime cd-1 installed\r\n\r\n");

        Assert.Equal("ok", result.Outcome);
        Assert.Equal(new[] { "runtime cd-1 installed" }, result.Lines);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n")]
    public void AnEmptyFileHasNoOutcome(string text)
    {
        var result = SetupHelperResult.Parse(text);

        Assert.Null(result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Empty(result.Lines);
    }
}

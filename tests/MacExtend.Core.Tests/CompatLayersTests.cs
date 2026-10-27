namespace MacExtend.Core.Tests;

public class CompatLayersTests
{
    [Theory]
    [InlineData(null, "~ HIGHDPIAWARE")]
    [InlineData("", "~ HIGHDPIAWARE")]
    [InlineData("~ RUNASADMIN", "~ RUNASADMIN HIGHDPIAWARE")]
    [InlineData("~ HIGHDPIAWARE", "~ HIGHDPIAWARE")]
    [InlineData("~ RUNASADMIN HIGHDPIAWARE", "~ RUNASADMIN HIGHDPIAWARE")]
    public void AddsApplicationDpiScalingOnce(string existing, string expected)
    {
        Assert.Equal(expected, CompatLayers.WithHighDpiAware(existing));
    }

    [Theory]
    [InlineData("~ DPIUNAWARE", "~ HIGHDPIAWARE")]
    [InlineData("~ GDIDPISCALING DPIUNAWARE", "~ HIGHDPIAWARE")]
    [InlineData("~ WIN8RTM DPIUNAWARE", "~ WIN8RTM HIGHDPIAWARE")]
    public void ReplacesConflictingSystemScaling(string existing, string expected)
    {
        Assert.Equal(expected, CompatLayers.WithHighDpiAware(existing));
    }

    [Fact]
    public void KeepsOtherFlagsWithoutTildeAndNormalisesSpacing()
    {
        Assert.Equal("~ WIN7RTM HIGHDPIAWARE", CompatLayers.WithHighDpiAware("  WIN7RTM  "));
    }
}

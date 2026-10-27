namespace MacExtend.Core.Tests;

public class DeviceNameTests
{
    [Theory]
    [InlineData("Máy của Thịnh", "May cua Thinh")]
    [InlineData("Đường", "Duong")]
    [InlineData("a\"b;c", "abc")]
    [InlineData("  Laptop   phong   ngu  ", "Laptop phong ngu")]
    [InlineData("Dell (XPS) 13_v2.0 - Lan's", "Dell (XPS) 13_v2.0 - Lan's")]
    public void SanitizeFoldsToSafeAscii(string input, string expected)
    {
        Assert.Equal(expected, DeviceName.Sanitize(input, "Fallback"));
    }

    [Fact]
    public void SanitizeCutsToMaxLength()
    {
        var result = DeviceName.Sanitize(new string('x', 60), "Fallback");

        Assert.Equal(DeviceName.MaxLength, result.Length);
    }

    [Fact]
    public void SanitizeDoesNotEndWithSpaceAfterCut()
    {
        var result = DeviceName.Sanitize(new string('x', 39) + " yyyy", "Fallback");

        Assert.Equal(new string('x', 39), result);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("日本")]
    public void SanitizeUsesFallbackWhenNothingUsableRemains(string input)
    {
        Assert.Equal("May Win", DeviceName.Sanitize(input, "Máy Win"));
    }

    [Fact]
    public void SanitizeUsesProductNameWhenFallbackAlsoEmpty()
    {
        Assert.Equal("MacExtend", DeviceName.Sanitize("日本", "中文"));
    }

    [Fact]
    public void DefaultForPrefixesProductName()
    {
        Assert.Equal("MacExtend-DESKTOP-AB12", DeviceName.DefaultFor("DESKTOP-ÂB12"));
    }
}

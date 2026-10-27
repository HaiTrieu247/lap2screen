using System;

namespace MacExtend.Core.Tests;

public class PinTests
{
    [Fact]
    public void GenerateAlwaysGivesValidFourDigitPins()
    {
        var rng = new Random(42);
        for (var i = 0; i < 1000; i++)
        {
            var pin = Pin.Generate(rng);
            Assert.Equal(4, pin.Length);
            Assert.True(Pin.IsValid(pin), pin);
        }
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("0042", true)]
    [InlineData("0000", false)]
    [InlineData("12a4", false)]
    [InlineData("123", false)]
    [InlineData("12345", false)]
    [InlineData("١٢٣٤", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidAcceptsOnlyFourAsciiDigits(string pin, bool expected)
    {
        Assert.Equal(expected, Pin.IsValid(pin));
    }
}

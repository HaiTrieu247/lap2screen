namespace MacExtend.Core.Tests;

public class FirewallStatusParserTests
{
    [Fact]
    public void AllowWithoutBlockIsOk()
    {
        var s = FirewallStatusParser.Parse("ALLOW=1\r\nBLOCK=0\r\n");

        Assert.True(s.HasAllow);
        Assert.False(s.HasBlock);
        Assert.True(s.Ok);
    }

    [Fact]
    public void MissingAllowIsNotOk()
    {
        Assert.False(FirewallStatusParser.Parse("ALLOW=0\nBLOCK=0").Ok);
    }

    [Fact]
    public void BlockRuleIsReportedEvenWithAllow()
    {
        var s = FirewallStatusParser.Parse("ALLOW=1\nBLOCK=2");

        Assert.True(s.HasBlock);
        Assert.False(s.Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Get-NetFirewallRule : Access denied")]
    public void GarbageIsNothingKnown(string output)
    {
        var s = FirewallStatusParser.Parse(output);

        Assert.False(s.HasAllow);
        Assert.False(s.HasBlock);
        Assert.False(s.IsKnown);
    }

    [Fact]
    public void BothLinesPresentIsKnownEvenWithZeroRules()
    {
        Assert.True(FirewallStatusParser.Parse("ALLOW=0\nBLOCK=0").IsKnown);
    }

    [Fact]
    public void OneLineMissingIsNotKnown()
    {
        Assert.False(FirewallStatusParser.Parse("ALLOW=1").IsKnown);
    }
}

public class NetworkProfileParserTests
{
    [Fact]
    public void ParsesEachConnection()
    {
        var list = NetworkProfileParser.Parse("12|Wi-Fi|Public\r\n5|Ethernet 2|Private\r\n");

        Assert.Equal(2, list.Count);
        Assert.Equal(12, list[0].InterfaceIndex);
        Assert.Equal("Wi-Fi", list[0].Alias);
        Assert.True(list[0].IsPublic);
        Assert.Equal("Ethernet 2", list[1].Alias);
        Assert.False(list[1].IsPublic);
    }

    [Fact]
    public void DomainNetworkIsNotPublic()
    {
        Assert.False(NetworkProfileParser.Parse("3|Corp|DomainAuthenticated")[0].IsPublic);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("garbage\nx|y|Public\n|Wi-Fi|Public")]
    public void SkipsUnparseableLines(string output)
    {
        Assert.Empty(NetworkProfileParser.Parse(output));
    }
}

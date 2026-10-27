namespace MacExtend.Core.Tests;

public class ConnectionTrackerTests
{
    private static readonly LogEvent Connecting =
        UxPlayLogParser.Parse("connection request from Thinh's MacBook (Mac14,2) with deviceID = AA");

    private static readonly LogEvent PhoneConnecting =
        UxPlayLogParser.Parse("connection request from Thịnh’s iPhone (iPhone15,2) with deviceID = BB");

    private static readonly LogEvent Lost =
        UxPlayLogParser.Parse("*** ERROR lost connection with client (network problem?)");

    [Fact]
    public void ConnectingLogMarksConnectedWithName()
    {
        var t = new ConnectionTracker();

        Assert.True(t.OnLog(Connecting));
        Assert.True(t.IsConnected);
        Assert.Equal("Thinh's MacBook", t.ClientName);
    }

    [Fact]
    public void OneZeroSampleKeepsConnection()
    {
        var t = new ConnectionTracker();
        t.OnLog(Connecting);

        Assert.False(t.OnTcpSample(0));
        Assert.True(t.IsConnected);
    }

    [Fact]
    public void TwoConsecutiveZeroSamplesDisconnect()
    {
        var t = new ConnectionTracker();
        t.OnLog(Connecting);
        t.OnTcpSample(0);

        Assert.True(t.OnTcpSample(0));
        Assert.False(t.IsConnected);
        Assert.Null(t.ClientName);
    }

    [Fact]
    public void PositiveSampleResetsZeroCount()
    {
        var t = new ConnectionTracker();
        t.OnLog(Connecting);
        t.OnTcpSample(0);
        t.OnTcpSample(3);
        t.OnTcpSample(0);

        Assert.True(t.IsConnected);
    }

    [Fact]
    public void LostConnectionDisconnectsImmediately()
    {
        var t = new ConnectionTracker();
        t.OnLog(Connecting);

        Assert.True(t.OnLog(Lost));
        Assert.False(t.IsConnected);
    }

    [Fact]
    public void TcpSamplesAloneNeverConnect()
    {
        var t = new ConnectionTracker();

        Assert.False(t.OnTcpSample(5));
        Assert.False(t.IsConnected);
    }

    [Fact]
    public void UnrelatedLogDoesNotChangeState()
    {
        var t = new ConnectionTracker();

        Assert.False(t.OnLog(UxPlayLogParser.Parse("hello")));
    }

    [Fact]
    public void ResetClearsConnection()
    {
        var t = new ConnectionTracker();
        t.OnLog(Connecting);

        t.Reset();

        Assert.False(t.IsConnected);
        Assert.Null(t.ClientName);
    }

    [Fact]
    public void NewSourceTakesOverWithoutDisconnect()
    {
        // uxplay runs with -nohold: an iPhone can replace a Mac session and no disconnect is logged in between
        var t = new ConnectionTracker();
        t.OnLog(Connecting);

        Assert.True(t.OnLog(PhoneConnecting));
        Assert.True(t.IsConnected);
        Assert.Equal("Thịnh’s iPhone", t.ClientName);
        Assert.False(t.OnLog(PhoneConnecting));
    }

    [Fact]
    public void ReconnectFromAnotherSourceAfterTcpDrop()
    {
        var t = new ConnectionTracker();
        t.OnLog(Connecting);
        t.OnTcpSample(0);
        t.OnTcpSample(0);

        Assert.Null(t.ClientName);
        Assert.True(t.OnLog(PhoneConnecting));
        Assert.True(t.IsConnected);
        Assert.Equal("Thịnh’s iPhone", t.ClientName);
    }

    [Fact]
    public void TakeoverRestartsZeroCount()
    {
        var t = new ConnectionTracker();
        t.OnLog(Connecting);
        t.OnTcpSample(0);
        t.OnLog(PhoneConnecting);

        Assert.False(t.OnTcpSample(0));
        Assert.True(t.IsConnected);
        Assert.Equal("Thịnh’s iPhone", t.ClientName);
    }

    [Fact]
    public void ResetClearsPhoneSession()
    {
        var t = new ConnectionTracker();
        t.OnLog(PhoneConnecting);

        t.Reset();

        Assert.False(t.IsConnected);
        Assert.Null(t.ClientName);
        Assert.True(t.OnLog(PhoneConnecting));
    }
}

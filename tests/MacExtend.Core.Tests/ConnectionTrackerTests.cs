namespace MacExtend.Core.Tests;

public class ConnectionTrackerTests
{
    private static readonly LogEvent Connecting =
        UxPlayLogParser.Parse("connection request from Thinh's MacBook (Mac14,2) with deviceID = AA");

    private static readonly LogEvent PhoneConnecting =
        UxPlayLogParser.Parse("connection request from Thịnh’s iPhone (iPhone15,2) with deviceID = BB");

    private static readonly LogEvent Lost =
        UxPlayLogParser.Parse("*** ERROR lost connection with client (network problem?)");

    private static readonly LogEvent WindowClosed =
        UxPlayLogParser.Parse("GStreamer error (video): d3d11videosink0 Output window was closed");

    [Fact]
    public void ClosingTheVideoWindowDisconnectsImmediately()
    {
        // uxplay drops the client and rebuilds the renderer in the same process; TCP may linger for a moment
        var t = new ConnectionTracker();
        t.OnLog(PhoneConnecting);

        Assert.True(t.OnLog(WindowClosed));
        Assert.False(t.IsConnected);
        Assert.Null(t.ClientName);
    }

    [Fact]
    public void RepeatedWindowClosedIsHarmless()
    {
        var t = new ConnectionTracker();
        t.OnLog(Connecting);
        t.OnLog(WindowClosed);

        Assert.False(t.OnLog(WindowClosed));
        Assert.False(t.IsConnected);
        Assert.Null(t.ClientName);
    }

    [Fact]
    public void WindowClosedWhileIdleChangesNothing()
    {
        var t = new ConnectionTracker();

        Assert.False(t.OnLog(WindowClosed));
        Assert.False(t.IsConnected);
    }

    [Fact]
    public void ReconnectAfterClosingTheVideoWindow()
    {
        var t = new ConnectionTracker();
        t.OnLog(PhoneConnecting);
        t.OnLog(WindowClosed);

        Assert.True(t.OnLog(PhoneConnecting));
        Assert.True(t.IsConnected);
        Assert.Equal("Thịnh’s iPhone", t.ClientName);
    }

    [Fact]
    public void SessionAfterWindowClosedNeedsTwoFreshZeroSamples()
    {
        // a zero sample from before the close must not count towards ending the next session
        var t = new ConnectionTracker();
        t.OnLog(Connecting);
        t.OnTcpSample(0);
        t.OnLog(WindowClosed);
        t.OnLog(PhoneConnecting);

        Assert.False(t.OnTcpSample(0));
        Assert.True(t.IsConnected);
        Assert.True(t.OnTcpSample(0));
        Assert.False(t.IsConnected);
    }

    [Fact]
    public void TcpStillEndsSessionWhenCloseLogIsLate()
    {
        var t = new ConnectionTracker();
        t.OnLog(PhoneConnecting);
        t.OnTcpSample(0);

        Assert.True(t.OnTcpSample(0));
        Assert.False(t.IsConnected);
        Assert.False(t.OnLog(WindowClosed));
        Assert.Null(t.ClientName);
    }

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

public class ConnectionTrackerPictureTests
{
    private static readonly LogEvent Phone = UxPlayLogParser.Parse("connection request from iPhone (iPhone12,1) with deviceID = AA");
    private static readonly LogEvent Pad = UxPlayLogParser.Parse("connection request from iPad (iPad13,4) with deviceID = BB");
    private static readonly LogEvent Lost = UxPlayLogParser.Parse("*** ERROR lost connection with client (network problem?)");
    private static readonly LogEvent Portrait = UxPlayLogParser.Parse("raop_rtp_mirror width_source = 828.000000 height_source = 1792.000000 x_offset = 0.000000 y_offset = 0.000000 width = 498.000000 height = 1080.000000");
    private static readonly LogEvent Landscape = UxPlayLogParser.Parse("raop_rtp_mirror width_source = 1792.000000 height_source = 828.000000 x_offset = 0.000000 y_offset = 0.000000 width = 1920.000000 height = 888.000000");

    [Fact]
    public void ThePictureSizeFollowsTheLatestLineAndSaysWhenItChanged()
    {
        var t = new ConnectionTracker();
        t.OnLog(Phone);

        Assert.True(t.OnPictureSize(Portrait));
        Assert.Equal(498, t.PictureWidth);
        Assert.Equal(1080, t.PictureHeight);
        Assert.False(t.OnPictureSize(Portrait));
        Assert.True(t.OnPictureSize(Landscape));
        Assert.Equal(1920, t.PictureWidth);
        Assert.Equal(888, t.PictureHeight);
    }

    [Fact]
    public void ThePictureSizeIsNotAConnectionChange()
    {
        var t = new ConnectionTracker();
        t.OnLog(Phone);

        Assert.False(t.OnLog(Portrait));
        Assert.True(t.IsConnected);
    }

    [Fact]
    public void ADisconnectOrAnotherDeviceForgetsThePictureSize()
    {
        var t = new ConnectionTracker();
        t.OnLog(Phone);
        t.OnPictureSize(Portrait);
        t.OnLog(Pad);
        Assert.Equal(0, t.PictureWidth);

        t.OnPictureSize(Landscape);
        t.OnLog(Lost);
        Assert.Equal(0, t.PictureWidth);
        Assert.Equal(0, t.PictureHeight);
    }

    [Fact]
    public void OtherLinesAreNotAPictureSize()
    {
        var t = new ConnectionTracker();

        Assert.False(t.OnPictureSize(Phone));
        Assert.Equal(0, t.PictureWidth);
    }
}

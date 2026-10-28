namespace MacExtend.Core.Tests;

public class UxPlayLogParserTests
{
    [Fact]
    public void ConnectionRequestGivesClientNameAndModel()
    {
        var e = UxPlayLogParser.Parse("connection request from MacBook Pro của Thịnh (Mac15,6) with deviceID = AA:BB:CC:DD:EE:FF");

        Assert.Equal(LogEventKind.ClientConnecting, e.Kind);
        Assert.Equal("MacBook Pro của Thịnh", e.ClientName);
        Assert.Equal("Mac15,6", e.ClientModel);
    }

    [Fact]
    public void ConnectionRequestKeepsParenthesesInsideName()
    {
        var e = UxPlayLogParser.Parse("connection request from Mac (work) (MacBookAir10,1) with deviceID = 11:22");

        Assert.Equal("Mac (work)", e.ClientName);
        Assert.Equal("MacBookAir10,1", e.ClientModel);
    }

    [Theory]
    [InlineData("connection request from Thịnh’s iPhone (iPhone15,2) with deviceID = AA", "Thịnh’s iPhone", "iPhone15,2")]
    [InlineData("connection request from iPad của Thịnh (iPad13,4) with deviceID = 11:22:33:44:55:66", "iPad của Thịnh", "iPad13,4")]
    public void PhoneAndTabletRequestsGiveClientNameAndModel(string line, string name, string model)
    {
        var e = UxPlayLogParser.Parse(line);

        Assert.Equal(LogEventKind.ClientConnecting, e.Kind);
        Assert.Equal(name, e.ClientName);
        Assert.Equal(model, e.ClientModel);
    }

    [Theory]
    [InlineData("*** ERROR lost connection with client (network problem?)")]
    [InlineData("***ERROR lost connection with client (network problem?)")]
    public void LostConnectionIsRecognised(string line)
    {
        Assert.Equal(LogEventKind.ConnectionLost, UxPlayLogParser.Parse(line).Kind);
    }

    [Theory]
    [InlineData("*** ERROR: GStreamer gst_parse_launch failed to create video pipeline 1")]
    [InlineData("*** ERROR: Error initializing raop!")]
    public void ErrorsAreRecognisedWithText(string line)
    {
        var e = UxPlayLogParser.Parse(line);

        Assert.Equal(LogEventKind.Error, e.Kind);
        Assert.Equal(line, e.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("UxPlay 1.74: An Open-Source AirPlay mirroring and audio-streaming server.")]
    [InlineData("*** WARNING: something minor")]
    public void OtherLinesAreNone(string line)
    {
        Assert.Equal(LogEventKind.None, UxPlayLogParser.Parse(line).Kind);
    }

    [Theory]
    // renderers/video_renderer.c logs every video bus error as "GStreamer error (video): <element> <message>"
    [InlineData("GStreamer error (video): d3d11videosink0 Output window was closed")]
    [InlineData("GStreamer error (video): d3d11videosink_h264 Output window was closed")]
    [InlineData("GStreamer error (video): d3d11videosink_h265 Output window was closed")]
    public void ClosedVideoWindowIsRecognised(string line)
    {
        var e = UxPlayLogParser.Parse(line);

        Assert.Equal(LogEventKind.VideoWindowClosed, e.Kind);
        Assert.Equal(line, e.Text);
        Assert.Null(e.ClientName);
    }

    [Theory]
    [InlineData("*** ERROR: GStreamer error (video): d3d11videosink0 Output window was closed")]
    [InlineData("GStreamer error (audio): wasapisink0 Output window was closed")]
    [InlineData("gstreamer error (video): d3d11videosink0 Output window was closed")]
    [InlineData("GStreamer error (video): d3d11videosink0 output window was closed")]
    [InlineData("GStreamer error (video): d3d11videosink0 Output window was opened")]
    [InlineData("GStreamer error (video): d3d11videosink0 Internal data stream error")]
    [InlineData("GStreamer error (video): d3d11videosink0 Window closed")]
    [InlineData("Output window was closed")]
    public void SimilarLinesAreNotAClosedVideoWindow(string line)
    {
        Assert.NotEqual(LogEventKind.VideoWindowClosed, UxPlayLogParser.Parse(line).Kind);
    }

    [Theory]
    [InlineData("GStreamer error (audio): wasapisink0 Output window was closed")]
    [InlineData("GStreamer error (video): d3d11videosink0 Internal data stream error")]
    [InlineData("GStreamer error (video): d3d11videosink0 Output window was opened")]
    public void OtherGStreamerErrorsStayNone(string line)
    {
        Assert.Equal(LogEventKind.None, UxPlayLogParser.Parse(line).Kind);
    }
}

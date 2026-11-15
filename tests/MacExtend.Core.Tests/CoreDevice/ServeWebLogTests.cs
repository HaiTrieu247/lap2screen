using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class ServeWebLogTests
{
    private const string Prefix = "2026-10-29 10:00:00 LAPTOP-1 pymobiledevice3.remote.core_device.screen_stream[4242] ";
    private const string Main = "2026-10-29 10:00:00 LAPTOP-1 pymobiledevice3.__main__[4242] ";

    [Fact]
    public void ReadsTheOpenLineAsReadinessWithItsPort()
    {
        var line = ServeWebLog.Parse(Prefix + "INFO Open http://127.0.0.1:8123/ in Safari/Chrome. Ctrl-C to stop.");

        Assert.Equal(ServeWebLogKind.Listening, line.Kind);
        Assert.Equal(8123, line.Port);
    }

    [Fact]
    public void ReadsTheOpenLineThroughAnsiColors()
    {
        var line = ServeWebLog.Parse("\x1b[2m2026-10-29 10:00:00\x1b[0m host \x1b[36mpymobiledevice3.remote.core_device.screen_stream\x1b[0m\x1b[2m[1]\x1b[0m \x1b[1;32mINFO\x1b[0m Open http://127.0.0.1:8080/ in Safari/Chrome. Ctrl-C to stop.");

        Assert.Equal(ServeWebLogKind.Listening, line.Kind);
        Assert.Equal(8080, line.Port);
    }

    [Theory]
    [InlineData("INFO Open http://0.0.0.0:8080/ in Safari/Chrome. Ctrl-C to stop.")]
    [InlineData("INFO Open https://127.0.0.1:8080/ in Safari/Chrome. Ctrl-C to stop.")]
    [InlineData("INFO Open http://127.0.0.1:99999/ in Safari/Chrome. Ctrl-C to stop.")]
    [InlineData("WARNING Open http://127.0.0.1:8080/ in Safari/Chrome. Ctrl-C to stop.")]
    [InlineData("INFO Open http://127.0.0.1:8080/ in Safari/Chrome.")]
    public void DoesNotTakeOtherOpenLinesAsReadiness(string message)
    {
        Assert.NotEqual(ServeWebLogKind.Listening, ServeWebLog.Parse(Prefix + message).Kind);
    }

    [Fact]
    public void DoesNotTakeTheOpenLineFromAnotherLogger()
    {
        var line = ServeWebLog.Parse("2026-10-29 10:00:00 h some.other.module[1] INFO Open http://127.0.0.1:8080/ in Safari/Chrome. Ctrl-C to stop.");

        Assert.Equal(ServeWebLogKind.Other, line.Kind);
    }

    [Fact]
    public void KeepsOnlyAWellFormedCodecString()
    {
        Assert.Equal("hvc1.1.6.L153.B0", ServeWebLog.Parse(Prefix + "INFO WebCodecs codec string: hvc1.1.6.L153.B0").Detail);
        Assert.Equal(ServeWebLogKind.Other, ServeWebLog.Parse(Prefix + "INFO WebCodecs codec string: <script>").Kind);
    }

    [Theory]
    [InlineData("INFO shutdown complete", ServeWebLogKind.ShutdownComplete)]
    [InlineData("INFO hid worker started", ServeWebLogKind.HidWorkerStarted)]
    [InlineData("WARNING initial stream start timed out; serving anyway (will retry lazily)", ServeWebLogKind.StreamStartDelayed)]
    [InlineData("WARNING device 00008110-000A1234ABCD801E disconnected; waiting for tunneld to surface it again", ServeWebLogKind.DeviceDisconnected)]
    [InlineData("INFO device 00008110-000A1234ABCD801E back online via tunneld; resuming stream", ServeWebLogKind.DeviceReconnected)]
    public void ClassifiesStatusLines(string message, ServeWebLogKind kind)
    {
        var line = ServeWebLog.Parse(Prefix + message);

        Assert.Equal(kind, line.Kind);
        Assert.Null(line.Detail);
    }

    [Fact]
    public void NamesTheShutdownStepThatTimedOut()
    {
        var line = ServeWebLog.Parse(Prefix + "WARNING shutdown: stop_all_streams timed out after 6.0s");

        Assert.Equal(ServeWebLogKind.ShutdownStepFailed, line.Kind);
        Assert.Equal("stop_all_streams", line.Detail);
    }

    [Fact]
    public void NamesTheShutdownStepThatRaised()
    {
        var line = ServeWebLog.Parse(Prefix + "ERROR shutdown: _stop_hid raised");

        Assert.Equal(ServeWebLogKind.ShutdownStepFailed, line.Kind);
        Assert.Equal("_stop_hid", line.Detail);
    }

    [Fact]
    public void DropsUnknownShutdownStepNames()
    {
        var line = ServeWebLog.Parse(Prefix + "WARNING shutdown: something_else timed out after 3.0s");

        Assert.Equal(ServeWebLogKind.ShutdownStepFailed, line.Kind);
        Assert.Null(line.Detail);
    }

    [Theory]
    [InlineData("ERROR The device's camera or microphone is in use by another app (e.g. Camera or Voice Memos). Quit that app", ServeWebLogKind.MediaInUse)]
    [InlineData("ERROR Failed to start service. Possible reasons are:", ServeWebLogKind.ServiceUnavailable)]
    [InlineData("ERROR NoDeviceConnectedError", ServeWebLogKind.DeviceNotFound)]
    [InlineData("ERROR Device not found: tunneld (127.0.0.1:49151) serves no tunnel for udid X", ServeWebLogKind.DeviceNotFound)]
    [InlineData("ERROR something nobody expected", ServeWebLogKind.Error)]
    public void ClassifiesFatalErrors(string message, ServeWebLogKind kind)
    {
        var line = ServeWebLog.Parse(Main + message);

        Assert.Equal(kind, line.Kind);
        Assert.Null(line.Detail);
    }

    // `python -m pymobiledevice3` names the CLI's logger "__main__" (seen on the device, run 1 of 2026-10-29)
    [Fact]
    public void ReadsTheCliLoggerNamedMain()
    {
        var line = ServeWebLog.Parse("2026-10-29 21:47:49 LEGION __main__[23084] ERROR The device's camera or microphone is in use by another app (e.g. Camera or Voice Memos). Quit that app on the device, then retry");

        Assert.Equal(ServeWebLogKind.MediaInUse, line.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("random output")]
    [InlineData("2026-10-29 10:00:00 LAPTOP-1 pymobiledevice3.remote.core_device.screen_stream[4242] DEBUG enqueue /key body=b'{\"usages\":[4]}'")]
    public void EverythingElseIsOther(string line)
    {
        var parsed = ServeWebLog.Parse(line);

        Assert.Equal(ServeWebLogKind.Other, parsed.Kind);
        Assert.Null(parsed.Detail);
    }
}

/// <summary>The lines of src/MacExtend.App/Helpers/direct_input.py, which logs in pymobiledevice3's shape under its own name.</summary>
public class DirectInputLogTests
{
    private const string Prefix = "2026-11-15 10:00:00 localhost macextend.direct_input[4242] ";

    [Fact]
    public void TheListeningLineIsReadiness()
    {
        var line = ServeWebLog.Parse(Prefix + "INFO listening on http://127.0.0.1:50123/");

        Assert.Equal(ServeWebLogKind.Listening, line.Kind);
        Assert.Equal(50123, line.Port);
    }

    [Theory]
    [InlineData("INFO listening on http://0.0.0.0:50123/")]
    [InlineData("INFO listening on http://127.0.0.1:99999/")]
    [InlineData("WARNING listening on http://127.0.0.1:50123/")]
    public void OnlyLoopbackAtInfoIsReadiness(string rest)
    {
        Assert.NotEqual(ServeWebLogKind.Listening, ServeWebLog.Parse(Prefix + rest).Kind);
    }

    [Fact]
    public void TheEndOfItsShutdownIsKnown()
    {
        Assert.Equal(ServeWebLogKind.ShutdownComplete, ServeWebLog.Parse(Prefix + "INFO shutdown complete").Kind);
    }

    [Fact]
    public void TheStreamOpeningTimeIsKeptAsANumberOnly()
    {
        var line = ServeWebLog.Parse(Prefix + "INFO stream open after 1.37 s");

        Assert.Equal(ServeWebLogKind.InputStreamOpened, line.Kind);
        Assert.Equal("1.37", line.Detail);
    }

    [Theory]
    [InlineData("INFO stream closed (idle) after 25 s", "idle")]
    [InlineData("INFO stream closed (shutdown) after 3 s", "shutdown")]
    [InlineData("INFO stream closed (failed) after 0 s", "failed")]
    [InlineData("INFO stream closed (something else) after 3 s", null)]
    public void AClosedStreamSaysWhyFromAFixedList(string rest, string why)
    {
        var line = ServeWebLog.Parse(Prefix + rest);

        Assert.Equal(ServeWebLogKind.InputStreamClosed, line.Kind);
        Assert.Equal(why, line.Detail);
    }

    [Fact]
    public void ARefusedStreamIsNotFatal()
    {
        // the device's camera or microphone is busy: input waits, the session goes on
        var line = ServeWebLog.Parse(Prefix + "INFO stream refused: camera or microphone in use");

        Assert.Equal(ServeWebLogKind.InputStreamRefused, line.Kind);
    }

    [Theory]
    [InlineData("ERROR Device not found: tunneld serves no tunnel for this device", ServeWebLogKind.DeviceNotFound)]
    [InlineData("ERROR NoDeviceConnectedError: the device could not be reached", ServeWebLogKind.DeviceNotFound)]
    [InlineData("ERROR pymobiledevice3 is 11.48.0, direct_input.py needs 11.47.0", ServeWebLogKind.Error)]
    public void ItsErrorsAreFatalLikeServeWebs(string rest, ServeWebLogKind kind)
    {
        Assert.Equal(kind, ServeWebLog.Parse(Prefix + rest).Kind);
    }

    [Theory]
    [InlineData("WARNING stream did not open (TimeoutError)", "TimeoutError")]
    [InlineData("WARNING input failed (ConnectionTerminatedError); the stream is closed and opens again on the next input", "ConnectionTerminatedError")]
    public void AFailureKeepsOnlyTheErrorsTypeName(string rest, string type)
    {
        var line = ServeWebLog.Parse(Prefix + rest);

        Assert.Equal(ServeWebLogKind.InputStreamFailed, line.Kind);
        Assert.Equal(type, line.Detail);
    }

    [Fact]
    public void AnotherLoggerWithTheSameWordsIsNotIt()
    {
        Assert.Equal(ServeWebLogKind.Other, ServeWebLog.Parse("2026-11-15 10:00:00 localhost someone.else[1] INFO listening on http://127.0.0.1:50123/").Kind);
    }
}

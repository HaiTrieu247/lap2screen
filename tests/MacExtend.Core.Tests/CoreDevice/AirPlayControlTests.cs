using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class DirectLifetimeTests
{
    [Fact]
    public void RunsWhileAPhoneMirrorsAndTenSecondsMore()
    {
        var now = new DateTime(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc);
        var life = new DirectLifetime(() => now);

        Assert.False(life.ShouldRun(true, false));
        Assert.True(life.ShouldRun(true, true));
        Assert.True(life.ShouldRun(true, false));
        now = now.AddSeconds(9);
        Assert.True(life.ShouldRun(true, false));
        now = now.AddSeconds(2);
        Assert.False(life.ShouldRun(true, false));
    }

    [Fact]
    public void MirroringAgainWithinTheLingerKeepsItRunning()
    {
        var now = new DateTime(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc);
        var life = new DirectLifetime(() => now);
        life.ShouldRun(true, true);
        life.ShouldRun(true, false);
        now = now.AddSeconds(8);

        Assert.True(life.ShouldRun(true, true));
        now = now.AddSeconds(8);
        Assert.True(life.ShouldRun(true, false));
    }

    [Fact]
    public void SwitchingOffStopsAtOnce()
    {
        var life = new DirectLifetime(() => DateTime.UtcNow);
        life.ShouldRun(true, true);

        Assert.False(life.ShouldRun(false, true));
    }

    [Theory]
    [InlineData("iPhone12,1", true)]
    [InlineData("iPad13,4", true)]
    [InlineData("MacBookPro18,3", false)]
    [InlineData("Mac14,2", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyIPhonesAndIPads(string model, bool mobile)
    {
        Assert.Equal(mobile, DirectLifetime.IsMobileModel(model));
    }
}

public class RememberedDevicesTests
{
    private const string A = "00008030-001E185E343B802E";
    private const string B = "00008110-001A2B3C4D5E6F70";

    [Fact]
    public void ChoicesAreRememberedPerAirPlayName()
    {
        var text = RememberedDevices.Remember("", "iPhone của Thịnh", A);
        text = RememberedDevices.Remember(text, "iPad", B);
        text = RememberedDevices.Remember(text, "iPhone của Thịnh", B);

        Assert.Equal(B, RememberedDevices.Remembered(text, "iPhone của Thịnh"));
        Assert.Equal(B, RememberedDevices.Remembered(text, "iPad"));
        Assert.Null(RememberedDevices.Remembered(text, "Mac"));
        Assert.Null(RememberedDevices.Remembered(text, null));
        Assert.Equal(2, RememberedDevices.Parse(text).Count);
    }

    [Fact]
    public void NamesWithSeparatorsAndBadUdidsAreSafe()
    {
        var text = RememberedDevices.Remember("", "a|b=c\nd", A);

        Assert.Equal(A, RememberedDevices.Remembered(text, "a|b=c\nd"));
        Assert.Equal(text, RememberedDevices.Remember(text, "x", "not-a-udid"));
        Assert.Equal(text, RememberedDevices.Remember(text, "", B));
        Assert.Empty(RememberedDevices.Parse("junk|=|x=y"));
    }

    [Fact]
    public void AtMostSixteenNamesAreKept()
    {
        var text = "";
        for (var i = 0; i < 20; i++) text = RememberedDevices.Remember(text, "d" + i, A);

        Assert.Equal(RememberedDevices.MaxEntries, RememberedDevices.Parse(text).Count);
        Assert.Null(RememberedDevices.Remembered(text, "d0"));
        Assert.Equal(A, RememberedDevices.Remembered(text, "d19"));
    }
}

public class UsbDeviceListTests
{
    private const string A = "00008030-001E185E343B802E";
    private const string B = "00008110-001A2B3C4D5E6F70";

    [Fact]
    public void ReadsTheNameAndModelOfEachCableDevice()
    {
        // `usbmux list --usb`: lockdown's short_info per device, with print_json
        var json = "[\n    {\n        \"BuildVersion\": \"24C5\",\n        \"ConnectionType\": \"USB\",\n        \"DeviceClass\": \"iPhone\",\n"
                   + "        \"DeviceName\": \"iPhone của Thịnh\",\n        \"Identifier\": \"" + A + "\",\n        \"ProductType\": \"iPhone12,1\",\n"
                   + "        \"ProductVersion\": \"27.2\",\n        \"UniqueDeviceID\": \"" + A + "\"\n    },\n"
                   + "    {\"DeviceName\": null, \"Identifier\": \"" + B + "\", \"ProductType\": \"iPad13,4\"},\n"
                   + "    {\"DeviceName\": \"junk\", \"Identifier\": \"not-a-udid\"},\n    7\n]\n";

        var devices = DeviceQueries.ParseUsbDevices(json);

        Assert.Equal(2, devices.Count);
        Assert.Equal(A, devices[0].Udid);
        Assert.Equal("iPhone của Thịnh", devices[0].Name);
        Assert.Equal("iPhone12,1", devices[0].Model);
        Assert.Equal(B, devices[1].Udid);
        Assert.Null(devices[1].Name);
        Assert.Equal("iPad13,4", devices[1].Model);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("Traceback (most recent call last):")]
    public void NothingWhenTheOutputIsNotAList(string stdout)
    {
        Assert.Empty(DeviceQueries.ParseUsbDevices(stdout));
    }
}

public class DirectTargetTests
{
    private const string A = "00008030-001E185E343B802E";
    private const string B = "00008110-001A2B3C4D5E6F70";
    private static readonly string[] None = new string[0];

    private static DirectOpenPlan Pick(params string[] usb) => DirectOpen.Plan(usb, None, true, null);

    private static IReadOnlyList<UsbDevice> Named(params (string Udid, string Name, string Model)[] devices) =>
        devices.Select(d => new UsbDevice(d.Udid, d.Name, d.Model)).ToList();

    [Fact]
    public void ThePlanStandsUnlessItAsksToPick()
    {
        var open = DirectOpen.Plan(new[] { A }, None, true, null);

        Assert.Same(open, DirectTarget.Resolve(open, B, Named((B, "x", "y")), "x", "y"));
    }

    [Fact]
    public void TheDeviceRememberedForThisAirPlayNameIsOpened()
    {
        var plan = DirectTarget.Resolve(Pick(A, B), B.ToLowerInvariant(), null, "iPhone", "iPhone12,1");

        Assert.Equal(DirectOpenStep.Open, plan.Step);
        Assert.Equal(B, plan.Udid);
        Assert.Equal(CoreDeviceRoute.Usb, plan.Route);
    }

    [Fact]
    public void ADeviceGoneSinceItWasRememberedIsNotOpened()
    {
        var plan = DirectTarget.Resolve(Pick(A, B), "00008110-000000000000AAAA", null, "iPhone", "iPhone12,1");

        Assert.Equal(DirectOpenStep.Pick, plan.Step);
    }

    [Fact]
    public void TheOnlyDeviceNamedLikeTheAirPlayNameIsOpened()
    {
        var named = Named((A, "iPad", "iPad13,4"), (B, "iPhone của Thịnh", "iPhone12,1"));

        var plan = DirectTarget.Resolve(Pick(A, B), null, named, " iPhone CỦA Thịnh ", "iPhone15,2");

        Assert.Equal(B, plan.Udid);
    }

    [Fact]
    public void WithoutANameTheOnlyDeviceOfTheMirroringModelIsOpened()
    {
        var named = Named((A, "Máy 1", "iPad13,4"), (B, "Máy 2", "iPhone12,1"));

        var plan = DirectTarget.Resolve(Pick(A, B), null, named, "iPhone", "iPhone12,1");

        Assert.Equal(B, plan.Udid);
    }

    [Fact]
    public void TwoOfTheSameNameOrModelStillAsk()
    {
        var sameName = Named((A, "iPhone", "iPhone12,1"), (B, "iPhone", "iPhone12,1"));

        Assert.Equal(DirectOpenStep.Pick, DirectTarget.Resolve(Pick(A, B), null, sameName, "iPhone", "iPhone12,1").Step);
        Assert.Equal(DirectOpenStep.Pick, DirectTarget.Resolve(Pick(A, B), null, null, "iPhone", "iPhone12,1").Step);
    }

    [Fact]
    public void ANamedDeviceThatIsNotACandidateIsIgnored()
    {
        const string c = "00008120-001A2B3C4D5E6F71";
        var named = Named((c, "iPhone", "iPhone12,1"));

        Assert.Equal(DirectOpenStep.Pick, DirectTarget.Resolve(Pick(A, B), null, named, "iPhone", "iPhone12,1").Step);
    }
}

public class DirectControlTextsTests
{
    private static readonly SessionView Ready = SessionView.Of(SessionPhase.Ready, SessionFailure.None, CoreDeviceRoute.Usb, "00008030-001E185E343B802E", 8123, false);
    private static readonly SessionView Preparing = SessionView.Of(SessionPhase.Preparing, SessionFailure.None, CoreDeviceRoute.Usb, "00008030-001E185E343B802E", 0, false);

    [Fact]
    public void OffShowsNothing()
    {
        Assert.Equal("", DirectControlTexts.Status(DirectView.Off, SessionView.Idle, true));
    }

    [Fact]
    public void WaitingSaysWhatItWaitsForOrThatTheSetupIsMissing()
    {
        Assert.Contains("phản chiếu", DirectControlTexts.Status(DirectView.Of(DirectPhase.Waiting), SessionView.Idle, true));
        Assert.Equal(DirectControlTexts.NotSetUp, DirectControlTexts.Status(DirectView.Of(DirectPhase.Waiting), SessionView.Idle, false));
    }

    [Fact]
    public void ARunningSessionSpeaksForItself()
    {
        var running = DirectView.Of(DirectPhase.Running);

        Assert.Equal(Preparing.StatusText, DirectControlTexts.Status(running, Preparing, true));
        var ready = DirectControlTexts.Status(running, Ready, true);
        Assert.Contains("qua cáp", ready);
        Assert.Contains("Home", ready);
    }

    [Fact]
    public void AFailureSaysWhy()
    {
        var failed = DirectView.Failed(SessionFailure.DeviceNotFound);

        Assert.Equal(CoreDeviceMessages.Failure(SessionFailure.DeviceNotFound), DirectControlTexts.Status(failed, SessionView.Idle, true));
    }

    [Fact]
    public void WithoutTheRuntimeAFailureSaysToSetUp()
    {
        Assert.Equal(DirectControlTexts.NotSetUp, DirectControlTexts.Status(DirectView.Failed(SessionFailure.NoAppleService), SessionView.Idle, false));
    }

    [Fact]
    public void EveryPhaseHasText()
    {
        foreach (DirectPhase phase in Enum.GetValues(typeof(DirectPhase)))
        {
            if (phase == DirectPhase.Off) continue;
            var view = phase == DirectPhase.Failed ? DirectView.Failed(SessionFailure.Other) : DirectView.Of(phase);
            Assert.False(string.IsNullOrEmpty(DirectControlTexts.Status(view, Preparing, true)), phase.ToString());
        }
    }

    [Fact]
    public void TheTraySaysWhoIsControlledOrThatAChoiceWaits()
    {
        Assert.Equal("Đang điều khiển iPhone của Thịnh", DirectControlTexts.TrayStatus(DirectView.Of(DirectPhase.Running), Ready, "iPhone của Thịnh"));
        Assert.Equal("Đang điều khiển thiết bị", DirectControlTexts.TrayStatus(DirectView.Of(DirectPhase.Running), Ready, null));
        Assert.Null(DirectControlTexts.TrayStatus(DirectView.Of(DirectPhase.Running), Preparing, "iPhone"));
        Assert.Equal(DirectControlTexts.ChooseDevice, DirectControlTexts.TrayStatus(DirectView.Of(DirectPhase.Choose), SessionView.Idle, "iPhone"));
        Assert.Null(DirectControlTexts.TrayStatus(DirectView.Of(DirectPhase.Waiting), SessionView.Idle, "iPhone"));
    }

    private static SessionView View(SessionPhase phase, bool forced = false, SessionFailure failure = SessionFailure.None) =>
        SessionView.Of(phase, failure, CoreDeviceRoute.Usb, "00008030-001E185E343B802E", phase == SessionPhase.Ready ? 50123 : 0, forced);

    [Fact]
    public void AForcedStopIsToldOnceTheSessionIsOver()
    {
        Assert.True(DirectControlTexts.WarnForcedStop(View(SessionPhase.Stopped, forced: true)));
        Assert.True(DirectControlTexts.WarnForcedStop(View(SessionPhase.Failed, forced: true, failure: SessionFailure.Exited)));
        Assert.False(DirectControlTexts.WarnForcedStop(View(SessionPhase.Stopped)));
        Assert.False(DirectControlTexts.WarnForcedStop(View(SessionPhase.Ready, forced: true)));
        Assert.False(DirectControlTexts.WarnForcedStop(SessionView.Idle));
    }
}

public class TouchStreamTests
{
    /// <summary>Records each touch; each one is answered only when the test says so.</summary>
    private sealed class FakeTouches
    {
        public readonly List<(TouchKind Kind, int X, int Y)> Sent = new List<(TouchKind, int, int)>();
        private readonly Queue<TaskCompletionSource<bool>> _pending = new Queue<TaskCompletionSource<bool>>();

        public Task Send(TouchKind kind, int x, int y)
        {
            lock (Sent)
            {
                Sent.Add((kind, x, y));
                var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending.Enqueue(answer);
                return answer.Task;
            }
        }

        public void AnswerOne(Exception error = null)
        {
            TaskCompletionSource<bool> next;
            lock (Sent) next = _pending.Dequeue();
            if (error == null) next.SetResult(true);
            else next.SetException(error);
        }

        public int Count
        {
            get { lock (Sent) return Sent.Count; }
        }
    }

    /// <summary>A clock the test moves; a delay completes once the clock reaches its end.</summary>
    private sealed class FakeClock
    {
        private readonly List<(TimeSpan Due, TaskCompletionSource<bool> Done)> _waits = new List<(TimeSpan, TaskCompletionSource<bool>)>();
        public TimeSpan Now;
        public readonly List<TimeSpan> Asked = new List<TimeSpan>();

        public Task Delay(TimeSpan span)
        {
            lock (_waits)
            {
                Asked.Add(span);
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waits.Add((Now + span, done));
                return done.Task;
            }
        }

        public void Advance(TimeSpan by)
        {
            List<TaskCompletionSource<bool>> due;
            lock (_waits)
            {
                Now += by;
                due = _waits.Where(w => w.Due <= Now).Select(w => w.Done).ToList();
                _waits.RemoveAll(w => w.Due <= Now);
            }
            foreach (var d in due) d.SetResult(true);
        }
    }

    private static async Task Settle(TouchStream stream)
    {
        // the stream's own work runs on the pool: let it reach its next wait. This returns as soon as it has; the bound
        // is only there so a broken stream fails its assertions instead of hanging, and a busy runner has time to spare
        for (var i = 0; i < 2000 && !stream.IsWaiting; i++) await Task.Delay(5);
    }

    private static (TouchStream, FakeTouches, FakeClock, List<Exception>) Create()
    {
        var touches = new FakeTouches();
        var clock = new FakeClock();
        var errors = new List<Exception>();
        var stream = new TouchStream(touches.Send, () => clock.Now, clock.Delay, e => { lock (errors) errors.Add(e); });
        return (stream, touches, clock, errors);
    }

    [Fact]
    public async Task ATapIsAContactThenARelease()
    {
        var (stream, touches, _, _) = Create();

        stream.Down(10, 20);
        stream.Up(10, 20);
        await Settle(stream);
        touches.AnswerOne();
        await Settle(stream);
        touches.AnswerOne();
        await Settle(stream);

        Assert.Equal(new[] { (TouchKind.Contact, 10, 20), (TouchKind.Release, 10, 20) }, touches.Sent);
        Assert.False(stream.IsDown);
    }

    [Fact]
    public async Task MovesWhileAnAnswerIsAwaitedCollapseIntoTheLatest()
    {
        var (stream, touches, clock, _) = Create();
        stream.Down(0, 0);
        await Settle(stream);

        stream.Move(1, 1);
        stream.Move(2, 2);
        stream.Move(3, 3);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        touches.AnswerOne();
        await Settle(stream);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        await Settle(stream);

        Assert.Equal(new[] { (TouchKind.Contact, 0, 0), (TouchKind.Contact, 3, 3) }, touches.Sent);
    }

    [Fact]
    public async Task MovesGoOutAtMostOncePerFrame()
    {
        var (stream, touches, clock, _) = Create();
        stream.Down(0, 0);
        await Settle(stream);
        touches.AnswerOne();
        await Settle(stream);

        stream.Move(5, 5);
        await Settle(stream);
        Assert.Equal(2, touches.Count);
        touches.AnswerOne();
        await Settle(stream);

        // the next move right away waits for the rest of its frame, and takes in the moves meanwhile
        stream.Move(6, 6);
        await Settle(stream);
        Assert.Equal(2, touches.Count);
        Assert.Contains(TouchStream.Frame, clock.Asked);
        stream.Move(7, 7);
        clock.Advance(TouchStream.Frame);
        await Settle(stream);

        Assert.Equal((TouchKind.Contact, 7, 7), touches.Sent[2]);
        Assert.Equal(3, touches.Count);
    }

    [Fact]
    public async Task TheReleaseWaitsForNoFrameAndComesLast()
    {
        var (stream, touches, clock, _) = Create();
        stream.Down(0, 0);
        await Settle(stream);
        stream.Move(4, 4);
        stream.Up(9, 9);
        touches.AnswerOne();
        await Settle(stream);
        clock.Advance(TouchStream.Frame);
        await Settle(stream);
        touches.AnswerOne();
        await Settle(stream);

        Assert.Equal(TouchKind.Release, touches.Sent.Last().Kind);
        Assert.Equal((9, 9), (touches.Sent.Last().X, touches.Sent.Last().Y));
    }

    [Fact]
    public async Task MovesWithoutAFingerDownAreIgnored()
    {
        var (stream, touches, _, _) = Create();

        stream.Move(1, 1);
        stream.Up(1, 1);
        await Settle(stream);

        Assert.Empty(touches.Sent);
    }

    [Fact]
    public async Task CancelLiftsTheFingerAtItsLastPoint()
    {
        var (stream, touches, clock, _) = Create();
        stream.Down(1, 1);
        await Settle(stream);
        touches.AnswerOne();
        await Settle(stream);
        stream.Move(8, 8);
        await Settle(stream);
        touches.AnswerOne();
        await Settle(stream);

        stream.Cancel();
        clock.Advance(TouchStream.Frame);
        await Settle(stream);

        Assert.Equal((TouchKind.Release, 8, 8), touches.Sent.Last());
        Assert.False(stream.IsDown);
        stream.Cancel();
        await Settle(stream);
        Assert.Equal(1, touches.Sent.Count(t => t.Kind == TouchKind.Release));
    }

    [Fact]
    public async Task AFailedTouchIsReportedAndTheNextOneStillGoes()
    {
        var (stream, touches, _, errors) = Create();
        stream.Down(1, 1);
        stream.Up(1, 1);
        await Settle(stream);
        touches.AnswerOne(new InvalidOperationException("closed"));
        await Settle(stream);
        touches.AnswerOne();
        await Settle(stream);

        Assert.Single(errors);
        Assert.Equal(2, touches.Count);
    }
}

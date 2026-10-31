using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class CoreDeviceGesturesTests
{
    /// <summary>Records each body with the time it arrived; <see cref="Answer"/> decides how long the answer takes.</summary>
    private sealed class SlowHandler : HttpMessageHandler
    {
        private readonly object _lock = new object();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<(string Body, TimeSpan At)> _requests = new List<(string Body, TimeSpan At)>();
        public Func<string, CancellationToken, Task> Answer = (_, _) => Task.CompletedTask;

        public List<(string Body, TimeSpan At)> Requests
        {
            get { lock (_lock) return _requests.ToList(); }
        }

        public List<string> Bodies => Requests.Select(r => r.Body).ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_lock) _requests.Add((body, _clock.Elapsed));
            await Answer(body, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        }
    }

    private static readonly TimeSpan ReleaseDeadline = TimeSpan.FromSeconds(2);

    // longer than any gesture deadline here, so only the gesture's own deadline can cut it
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private static (CoreDeviceClient client, SlowHandler handler) Create()
    {
        var handler = new SlowHandler();
        return (new CoreDeviceClient(8123, handler, RequestTimeout), handler);
    }

    private static string Touch(string type, int x, int y) => $"{{\"type\":\"{type}\",\"x\":{x},\"y\":{y}}}";

    private static int X(string body) => int.Parse(Regex.Match(body, "\"x\":(\\d+)").Groups[1].Value);

    private static Task Never(CancellationToken token) => Task.Delay(Timeout.Infinite, token);

    [Fact]
    public async Task DragFollowsTheClockWhenAnswersAreSlow()
    {
        var (client, handler) = Create();
        handler.Answer = (_, token) => Task.Delay(40, token);

        var watch = Stopwatch.StartNew();
        var result = await CoreDeviceGestures.DragAsync(client, 0, 1000, 65535, 1000, TimeSpan.FromMilliseconds(400), ReleaseDeadline, CancellationToken.None);
        watch.Stop();

        Assert.Equal(GestureEnd.Completed, result.End);
        Assert.Equal(ReleaseOutcome.NothingHeld, result.Cleanup);
        // paced per step instead (16 ms, then a 40 ms answer, for 25 steps) it would take about 1.4 s
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(1000));
        var bodies = handler.Bodies;
        Assert.Equal(Touch("contact", 0, 1000), bodies[0]);
        Assert.Equal(Touch("contact", 65535, 1000), bodies[^2]);
        Assert.Equal(Touch("release", 65535, 1000), bodies[^1]);
        var xs = bodies.Select(X).ToList();
        Assert.Equal(xs.OrderBy(x => x), xs);
        // slow answers drop frames rather than queue them
        Assert.True(bodies.Count < 400 / 16 + 3, $"{bodies.Count} requests");
        Assert.True(client.IsOpen);
        Assert.False(client.ContactMayBeHeld);
    }

    [Fact]
    public async Task DragOnFastAnswersEndsAtItsDuration()
    {
        var (client, handler) = Create();

        var watch = Stopwatch.StartNew();
        var result = await CoreDeviceGestures.DragAsync(client, 65535, 0, 0, 65535, TimeSpan.FromMilliseconds(200), ReleaseDeadline, CancellationToken.None);

        Assert.Equal(GestureEnd.Completed, result.End);
        // the top only rules out a drag that waits on something; a shared CI runner has taken 780 ms for this 200 ms one
        // (the first HttpClient call's JIT, 15.6 ms timer ticks, the other test classes running in parallel)
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(1500));
        var bodies = handler.Bodies;
        Assert.Equal(Touch("contact", 65535, 0), bodies[0]);
        Assert.Equal(Touch("contact", 0, 65535), bodies[^2]);
        Assert.Equal(Touch("release", 0, 65535), bodies[^1]);
        Assert.True(bodies.Count <= 200 / 16 + 3, $"{bodies.Count} requests");
    }

    [Fact]
    public async Task HoldReleasesOnTheClockNotAfterTheAnswer()
    {
        var (client, handler) = Create();
        handler.Answer = (_, token) => Task.Delay(300, token);

        var result = await CoreDeviceGestures.HoldAsync(client, 10, 20, TimeSpan.FromMilliseconds(400), ReleaseDeadline, CancellationToken.None);

        Assert.Equal(GestureEnd.Completed, result.End);
        var requests = handler.Requests;
        Assert.Equal(new[] { Touch("contact", 10, 20), Touch("release", 10, 20) }, requests.Select(r => r.Body));
        // waiting 400 ms after the 300 ms answer would put the release 700 ms after the contact
        Assert.InRange(requests[1].At - requests[0].At, TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(600));
        Assert.False(client.ContactMayBeHeld);
    }

    [Fact]
    public async Task ADragStillRunningPastItsDeadlineIsCutAndReleased()
    {
        var (client, handler) = Create();
        // contacts are never answered; releases are, at once
        handler.Answer = (body, token) => body.Contains("\"contact\"") ? Never(token) : Task.CompletedTask;

        var watch = Stopwatch.StartNew();
        var result = await CoreDeviceGestures.DragAsync(client, 100, 200, 300, 400, TimeSpan.FromMilliseconds(200), ReleaseDeadline, CancellationToken.None);

        Assert.Equal(GestureEnd.CutAtDeadline, result.End);
        Assert.Equal(ReleaseOutcome.Released, result.Cleanup);
        // 200 ms plus the 1 s slack, well before the 5 s request timeout
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(1100), TimeSpan.FromMilliseconds(2500));
        Assert.Equal(new[] { Touch("contact", 100, 200), Touch("release", 100, 200) }, handler.Bodies);
        // the cut caught the contact in flight: its fate is unknown, so the session takes no more input
        Assert.False(client.IsOpen);
        Assert.False(client.ContactMayBeHeld);
    }

    [Fact]
    public async Task AHoldWhoseReleaseHangsIsCutAndReleasedOnItsOwnDeadline()
    {
        var (client, handler) = Create();
        var releases = 0;
        handler.Answer = (body, token) => body.Contains("\"release\"") && Interlocked.Increment(ref releases) == 1 ? Never(token) : Task.CompletedTask;

        var watch = Stopwatch.StartNew();
        var result = await CoreDeviceGestures.HoldAsync(client, 7, 8, TimeSpan.FromMilliseconds(100), ReleaseDeadline, CancellationToken.None);

        Assert.Equal(GestureEnd.CutAtDeadline, result.End);
        Assert.Equal(ReleaseOutcome.Released, result.Cleanup);
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(2500));
        Assert.Equal(new[] { Touch("contact", 7, 8), Touch("release", 7, 8), Touch("release", 7, 8) }, handler.Bodies);
    }

    [Fact]
    public async Task StoppingAGestureLiftsTheFingerAndRethrows()
    {
        var (client, handler) = Create();
        using var stop = new CancellationTokenSource();

        var hold = CoreDeviceGestures.HoldAsync(client, 5, 6, TimeSpan.FromSeconds(5), ReleaseDeadline, stop.Token);
        while (handler.Requests.Count == 0) await Task.Delay(10);
        await Task.Delay(200);
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hold);
        Assert.Equal(new[] { Touch("contact", 5, 6), Touch("release", 5, 6) }, handler.Bodies);
        Assert.False(client.ContactMayBeHeld);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9001)]
    public async Task RejectsDurationsOutsideTheLimitBeforeTouching(int ms)
    {
        var (client, handler) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CoreDeviceGestures.HoldAsync(client, 1, 1, TimeSpan.FromMilliseconds(ms), ReleaseDeadline, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CoreDeviceGestures.DragAsync(client, 1, 1, 2, 2, TimeSpan.FromMilliseconds(ms), ReleaseDeadline, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RejectsADragEndPointOutsideTheHidRangeBeforeTouching()
    {
        var (client, handler) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CoreDeviceGestures.DragAsync(client, 1, 1, 65536, 2, TimeSpan.FromMilliseconds(100), ReleaseDeadline, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void TheLongestGestureWithItsSlackStaysWithinTenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), CoreDeviceGestures.MaxDuration + CoreDeviceGestures.Slack);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class CoreDeviceClientTests
{
    private sealed class Sent
    {
        public string Path;
        public string Method;
        public string Body;
        public bool? ExpectContinue;
        public string ContentType;
        public long? ContentLength;
        public Uri Uri;
    }

    /// <summary>Records every request; <see cref="Respond"/> decides each answer (default: 200 ok at once).</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly object _lock = new object();
        private int _inFlight;
        public readonly List<Sent> Requests = new List<Sent>();
        public int MaxConcurrent;
        public Func<Sent, CancellationToken, Task<HttpResponseMessage>> Respond =
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sent = new Sent
            {
                Path = request.RequestUri.AbsolutePath,
                Method = request.Method.Method,
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(),
                ExpectContinue = request.Headers.ExpectContinue,
                ContentType = request.Content?.Headers.ContentType?.MediaType,
                ContentLength = request.Content?.Headers.ContentLength,
                Uri = request.RequestUri,
            };
            lock (_lock)
            {
                Requests.Add(sent);
                _inFlight++;
                MaxConcurrent = Math.Max(MaxConcurrent, _inFlight);
            }
            try
            {
                return await Respond(sent, cancellationToken);
            }
            finally
            {
                lock (_lock) _inFlight--;
            }
        }
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    private static (CoreDeviceClient client, FakeHandler handler) Create(int port = 8123)
    {
        var handler = new FakeHandler();
        return (new CoreDeviceClient(port, handler, Timeout), handler);
    }

    private static Task<HttpResponseMessage> Hang(CancellationToken token) =>
        Task.Delay(System.Threading.Timeout.Infinite, token).ContinueWith<HttpResponseMessage>(_ => throw new TaskCanceledException());

    [Fact]
    public async Task ContactPostsTheTouchContract()
    {
        var (client, handler) = Create(8123);

        await client.ContactAsync(100, 65535, CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal(new Uri("http://127.0.0.1:8123/touch"), sent.Uri);
        Assert.Equal("{\"type\":\"contact\",\"x\":100,\"y\":65535}", sent.Body);
        Assert.Equal("application/json", sent.ContentType);
        Assert.Equal(sent.Body.Length, sent.ContentLength);
    }

    [Fact]
    public async Task RequestsNeverAskForContinue()
    {
        var (client, handler) = Create();

        await client.ContactAsync(1, 2, CancellationToken.None);
        await client.SetKeysAsync(new[] { 4 }, CancellationToken.None);
        await client.HomeAsync(CancellationToken.None);

        Assert.All(handler.Requests, r => Assert.False(r.ExpectContinue));
    }

    [Fact]
    public void DefaultHandlerSkipsProxiesRedirectsAndCookies()
    {
        using var handler = CoreDeviceClient.CreateHandler();

        Assert.False(handler.UseProxy);
        Assert.Null(handler.Proxy);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    [Fact]
    public async Task TapAndReleaseUseTheirTypes()
    {
        var (client, handler) = Create();

        await client.TapAsync(10, 20, CancellationToken.None);
        await client.ReleaseContactAsync(30, 40, CancellationToken.None);

        Assert.Equal("{\"type\":\"tap\",\"x\":10,\"y\":20}", handler.Requests[0].Body);
        Assert.Equal("{\"type\":\"release\",\"x\":30,\"y\":40}", handler.Requests[1].Body);
    }

    [Fact]
    public async Task KeysSendTheFullSortedSet()
    {
        var (client, handler) = Create();

        await client.SetKeysAsync(new[] { 0xE1, 0x04, 0x05 }, CancellationToken.None);
        await client.SetKeysAsync(Array.Empty<int>(), CancellationToken.None);

        Assert.Equal("/key", handler.Requests[0].Path);
        Assert.Equal("{\"usages\":[4,5,225]}", handler.Requests[0].Body);
        Assert.Equal("{\"usages\":[]}", handler.Requests[1].Body);
    }

    [Fact]
    public async Task HomePressesTheNamedButton()
    {
        var (client, handler) = Create();

        await client.HomeAsync(CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("/button", sent.Path);
        Assert.Equal("{\"name\":\"home\",\"state\":\"press\"}", sent.Body);
    }

    [Theory]
    [InlineData(DeviceButton.VolumeUp, "volume-up")]
    [InlineData(DeviceButton.VolumeDown, "volume-down")]
    [InlineData(DeviceButton.Home, "home")]
    public async Task ButtonsArePressedByServeWebsName(DeviceButton button, string name)
    {
        var (client, handler) = Create();

        await client.PressAsync(button, CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("/button", sent.Path);
        Assert.Equal("{\"name\":\"" + name + "\",\"state\":\"press\"}", sent.Body);
    }

    [Fact]
    public async Task AButtonThatIsNotOneIsRefusedBeforeSending()
    {
        var (client, handler) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.PressAsync((DeviceButton)99, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(65536, 0)]
    [InlineData(0, 65536)]
    public async Task RejectsCoordinatesOutsideTheHidRange(int x, int y)
    {
        var (client, handler) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ContactAsync(x, y, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.TapAsync(x, y, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ReleaseContactAsync(x, y, CancellationToken.None));
        Assert.Empty(handler.Requests);
        Assert.False(client.ContactMayBeHeld);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(0xE8)]
    [InlineData(240)]
    [InlineData(-4)]
    public async Task RejectsUsagesTheKeyboardReportCannotCarry(int usage)
    {
        var (client, handler) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.SetKeysAsync(new[] { 4, usage }, CancellationToken.None));
        Assert.Empty(handler.Requests);
        Assert.False(client.KeysMayBeHeld);
    }

    [Fact]
    public async Task RejectsRepeatedOrMissingUsages()
    {
        var (client, handler) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => client.SetKeysAsync(new[] { 4, 4 }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SetKeysAsync(null, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendsOneRequestAtATimeInCallOrder()
    {
        var (client, handler) = Create();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async (sent, token) =>
        {
            if (sent.Body.Contains("\"x\":1,")) await gate.Task;
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        var first = client.ContactAsync(1, 1, CancellationToken.None);
        var second = client.ContactAsync(2, 2, CancellationToken.None);
        var third = client.ReleaseContactAsync(3, 3, CancellationToken.None);
        await Task.Delay(50);
        Assert.Single(handler.Requests);

        gate.SetResult(true);
        await Task.WhenAll(first, second, third);

        Assert.Equal(1, handler.MaxConcurrent);
        Assert.Equal(new[] { "\"x\":1,", "\"x\":2,", "\"x\":3," }, handler.Requests.Select(r => r.Body.Substring(r.Body.IndexOf("\"x\"", StringComparison.Ordinal), 6)));
    }

    [Fact]
    public async Task AFailedRequestIsNotRetriedAndClosesTheClient()
    {
        var (client, handler) = Create();
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<CoreDeviceClientException>(() => client.ContactAsync(1, 2, CancellationToken.None));
        await Assert.ThrowsAsync<CoreDeviceClientException>(() => client.ContactAsync(3, 4, CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.False(client.IsOpen);
    }

    [Fact]
    public async Task ARedirectIsAFailure()
    {
        var (client, handler) = Create();
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found));

        await Assert.ThrowsAsync<CoreDeviceClientException>(() => client.HomeAsync(CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.False(client.IsOpen);
    }

    [Fact]
    public async Task ATimeoutLeavesTheContactMarkedHeldAndIsNotRetried()
    {
        var (client, handler) = Create();
        handler.Respond = (_, token) => Hang(token);

        var watch = Stopwatch.StartNew();
        var e = await Assert.ThrowsAsync<CoreDeviceClientException>(() => client.ContactAsync(5, 6, CancellationToken.None));

        Assert.Contains("timed out", e.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Single(handler.Requests);
        Assert.True(client.ContactMayBeHeld);
        Assert.False(client.IsOpen);
    }

    [Fact]
    public async Task KeysAreMarkedHeldBeforeTheyAreSent()
    {
        var (client, handler) = Create();
        handler.Respond = (_, token) => Hang(token);

        await Assert.ThrowsAsync<CoreDeviceClientException>(() => client.SetKeysAsync(new[] { 4 }, CancellationToken.None));

        Assert.True(client.KeysMayBeHeld);
    }

    [Fact]
    public async Task CancellingBeforeTheTurnSendsNothingAndKeepsTheClientOpen()
    {
        var (client, handler) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ContactAsync(1, 2, cts.Token));

        Assert.Empty(handler.Requests);
        Assert.False(client.ContactMayBeHeld);
        Assert.True(client.IsOpen);
    }

    [Fact]
    public async Task CancellingWhileQueuedStillWaitsForTheRequestAhead()
    {
        var (client, handler) = Create();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async (_, _) =>
        {
            await gate.Task;
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        using var cts = new CancellationTokenSource();

        var first = client.ContactAsync(1, 1, CancellationToken.None);
        var cancelled = client.ContactAsync(2, 2, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var third = client.ReleaseContactAsync(3, 3, CancellationToken.None);
        await Task.Delay(50);

        Assert.Single(handler.Requests);
        gate.SetResult(true);
        await Task.WhenAll(first, third);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1, handler.MaxConcurrent);
    }

    [Fact]
    public async Task CancellingInFlightClosesTheClientAndKeepsTheContactMarked()
    {
        var (client, handler) = Create();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = (_, token) =>
        {
            started.TrySetResult(true);
            return Hang(token);
        };
        using var cts = new CancellationTokenSource();

        var contact = client.ContactAsync(1, 2, cts.Token);
        await started.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => contact);
        Assert.True(client.ContactMayBeHeld);
        Assert.False(client.IsOpen);
    }

    [Fact]
    public async Task ASuccessfulReleaseClearsTheMarks()
    {
        var (client, _) = Create();

        await client.ContactAsync(1, 2, CancellationToken.None);
        await client.SetKeysAsync(new[] { 4 }, CancellationToken.None);
        Assert.True(client.ContactMayBeHeld);
        Assert.True(client.KeysMayBeHeld);

        await client.ReleaseContactAsync(1, 2, CancellationToken.None);
        await client.SetKeysAsync(Array.Empty<int>(), CancellationToken.None);

        Assert.False(client.ContactMayBeHeld);
        Assert.False(client.KeysMayBeHeld);
    }

    [Fact]
    public async Task TapLeavesNoContactHeld()
    {
        var (client, _) = Create();

        await client.TapAsync(1, 2, CancellationToken.None);

        Assert.False(client.ContactMayBeHeld);
    }

    [Fact]
    public async Task ReleaseAllAfterAFailureLiftsTheLastContactAndEveryKey()
    {
        var (client, handler) = Create();
        await client.SetKeysAsync(new[] { 4, 5 }, CancellationToken.None);
        handler.Respond = (_, token) => Hang(token);
        await Assert.ThrowsAsync<CoreDeviceClientException>(() => client.ContactAsync(700, 800, CancellationToken.None));
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        var outcome = await client.ReleaseAllAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ReleaseOutcome.Released, outcome);
        Assert.Equal("{\"type\":\"release\",\"x\":700,\"y\":800}", handler.Requests[2].Body);
        Assert.Equal("{\"usages\":[]}", handler.Requests[3].Body);
        Assert.False(client.ContactMayBeHeld);
        Assert.False(client.KeysMayBeHeld);
        Assert.False(client.IsOpen);
    }

    [Fact]
    public async Task ReleaseAllRunsAfterTheOperationTokenWasCancelled()
    {
        var (client, handler) = Create();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = (_, token) =>
        {
            started.TrySetResult(true);
            return Hang(token);
        };
        using var cts = new CancellationTokenSource();
        var hold = client.ContactAsync(9, 9, cts.Token);
        await started.Task;
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hold);
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        Assert.Equal(ReleaseOutcome.Released, await client.ReleaseAllAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("{\"type\":\"release\",\"x\":9,\"y\":9}", handler.Requests.Last().Body);
    }

    [Fact]
    public async Task ReleaseAllSendsNothingWhenNothingIsHeld()
    {
        var (client, handler) = Create();
        await client.TapAsync(1, 1, CancellationToken.None);

        Assert.Equal(ReleaseOutcome.NothingHeld, await client.ReleaseAllAsync(TimeSpan.FromSeconds(2)));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ReleaseAllGivesUpAtItsDeadlineAndForgetsTheMarks()
    {
        var (client, handler) = Create();
        await client.ContactAsync(1, 1, CancellationToken.None);
        handler.Respond = (_, token) => Hang(token);

        var watch = Stopwatch.StartNew();
        var outcome = await client.ReleaseAllAsync(TimeSpan.FromMilliseconds(300));

        Assert.Equal(ReleaseOutcome.Unconfirmed, outcome);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.False(client.ContactMayBeHeld);
        var count = handler.Requests.Count;
        Assert.Equal(ReleaseOutcome.NothingHeld, await client.ReleaseAllAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(count, handler.Requests.Count);
    }

    [Fact]
    public async Task ForcedReleaseSendsBothEvenWithNothingMarked()
    {
        var (client, handler) = Create();

        Assert.Equal(ReleaseOutcome.Released, await client.ReleaseAllAsync(TimeSpan.FromSeconds(2), force: true));

        Assert.Equal("{\"type\":\"release\",\"x\":32768,\"y\":32768}", handler.Requests[0].Body);
        Assert.Equal("{\"usages\":[]}", handler.Requests[1].Body);
    }

    [Fact]
    public async Task CloseRefusesNewInputButAllowsCleanup()
    {
        var (client, handler) = Create();
        await client.ContactAsync(4, 4, CancellationToken.None);

        client.Close();

        await Assert.ThrowsAsync<CoreDeviceClientException>(() => client.TapAsync(1, 1, CancellationToken.None));
        Assert.Single(handler.Requests);
        Assert.Equal(ReleaseOutcome.Released, await client.ReleaseAllAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task InputQueuedBehindAFailureIsNotSent()
    {
        var (client, handler) = Create();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async (_, _) =>
        {
            await gate.Task;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        };

        var first = client.ContactAsync(1, 1, CancellationToken.None);
        var second = client.ContactAsync(2, 2, CancellationToken.None);
        gate.SetResult(true);

        await Assert.ThrowsAsync<CoreDeviceClientException>(() => first);
        await Assert.ThrowsAsync<CoreDeviceClientException>(() => second);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1023)]
    [InlineData(65536)]
    public void RejectsInvalidPorts(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CoreDeviceClient(port, new FakeHandler(), Timeout));
    }
}

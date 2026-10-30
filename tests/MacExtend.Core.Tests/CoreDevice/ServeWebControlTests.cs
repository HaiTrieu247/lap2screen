using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class ServeWebControlTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<(string Method, string Path, string Body)> Requests = new List<(string, string, string)>();
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Answer = "{\"ok\":true}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            Requests.Add((request.Method.Method, request.RequestUri.AbsolutePath, body));
            return new HttpResponseMessage(Status) { Content = new StringContent(Answer) };
        }
    }

    [Fact]
    public async Task HomePressesTheButton()
    {
        var h = new Handler();
        using (var c = new ServeWebControl(8080, h, TimeSpan.FromSeconds(1))) await c.HomeAsync(CancellationToken.None);

        Assert.Equal(("POST", "/button", "{\"name\":\"home\",\"state\":\"press\"}"), h.Requests[0]);
    }

    [Fact]
    public async Task SendsVietnameseTextAsJson()
    {
        var h = new Handler();
        using (var c = new ServeWebControl(8080, h, TimeSpan.FromSeconds(1))) await c.SendTextAsync("Tiếng Việt \"x\"\n\\", CancellationToken.None);

        var (method, path, body) = h.Requests[0];
        Assert.Equal("POST", method);
        Assert.Equal("/clipboard", path);
        Assert.Equal("Tiếng Việt \"x\"\n\\", ((Dictionary<string, object>)MiniJson.Parse(body))["text"]);
    }

    [Fact]
    public async Task ReadsTheDeviceText()
    {
        var h = new Handler { Answer = "{\"text\": \"xin chào\", \"image\": null}" };
        using (var c = new ServeWebControl(8080, h, TimeSpan.FromSeconds(1)))
        {
            Assert.Equal("xin chào", await c.GetTextAsync(CancellationToken.None));
        }
        Assert.Equal("GET", h.Requests[0].Method);
    }

    [Fact]
    public async Task NoTextIsNull()
    {
        var h = new Handler { Answer = "{\"text\": null, \"image\": {\"type\": \"image/png\", \"size\": 3}}" };
        using (var c = new ServeWebControl(8080, h, TimeSpan.FromSeconds(1)))
        {
            Assert.Null(await c.GetTextAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task AFailedCallThrowsAndTheNextStillWorks()
    {
        var h = new Handler { Status = HttpStatusCode.InternalServerError };
        using (var c = new ServeWebControl(8080, h, TimeSpan.FromSeconds(1)))
        {
            await Assert.ThrowsAsync<CoreDeviceClientException>(() => c.HomeAsync(CancellationToken.None));
            h.Status = HttpStatusCode.OK;
            await c.HomeAsync(CancellationToken.None);
        }
    }

    private sealed class SilentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        }
    }

    [Fact]
    public async Task ACallThatOutlastsItsTimeoutFailsLikeAnyOther()
    {
        using (var c = new ServeWebControl(8080, new SilentHandler(), TimeSpan.FromMilliseconds(50)))
        {
            await Assert.ThrowsAsync<CoreDeviceClientException>(() => c.GetTextAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task TheCallersCancellationIsNotATimeout()
    {
        using (var cts = new CancellationTokenSource())
        using (var c = new ServeWebControl(8080, new SilentHandler(), TimeSpan.FromSeconds(30)))
        {
            var call = c.SendTextAsync("x", cts.Token);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        }
    }

    [Fact]
    public void TheClipboardWaitsLongerThanAButton()
    {
        // /clipboard goes to the device and back; over Wi-Fi that can take more than the 5 s a button gets
        Assert.Equal(TimeSpan.FromSeconds(5), ServeWebControl.DefaultTimeout);
        Assert.Equal(TimeSpan.FromSeconds(15), ServeWebControl.ClipboardTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsATimeoutThatIsNotPositive(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServeWebControl(8080, TimeSpan.FromMilliseconds(milliseconds)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServeWebControl(8080, new Handler(), TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Theory]
    [InlineData("a\u0001b", "\"a\\u0001b\"")]
    [InlineData("tab\t", "\"tab\\t\"")]
    public void EscapesControlCharacters(string text, string json)
    {
        Assert.Equal(json, ServeWebControl.JsonString(text));
    }
}

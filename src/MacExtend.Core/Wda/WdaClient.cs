using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MacExtend.Core.Wda
{
    /// <summary>A WDA request that failed. <see cref="Status"/> is null when WDA gave no answer at all.</summary>
    public sealed class WdaException : Exception
    {
        public WdaException(string message, int? status = null, string error = null) : base(message)
        {
            Status = status;
            Error = error;
        }

        public int? Status { get; }
        /// <summary>The W3C error code of the answer ("no such element", ...), or null.</summary>
        public string Error { get; }
        public bool NoAnswer => Status == null;
    }

    /// <summary>
    /// A minimal client of WebDriverAgent's HTTP API: one session, touches as W3C actions, typing, buttons. Keep-alive
    /// connections (each new one through the cable costs a usbmux handshake), no proxy, no redirects, a deadline on
    /// every request. Paths and bodies only; never logs what is typed. Use one request at a time.
    /// </summary>
    public sealed class WdaClient : IDisposable
    {
        public const int DevicePort = 8100;
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
        /// <summary>A touch action waits for the gesture to play out on the device; a drag lasts up to 3.5 s.</summary>
        public static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(20);

        private readonly HttpClient _http;
        private readonly Uri _base;

        public WdaClient(Uri baseUri, HttpMessageHandler handler = null)
        {
            _base = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
            if (_base.Scheme != Uri.UriSchemeHttp) throw new ArgumentException("WDA speaks plain http", nameof(baseUri));
            try
            {
                // .NET Framework: no "Expect: 100-continue" pause before every POST body, and no Nagle delay on small requests
                var point = ServicePointManager.FindServicePoint(_base);
                point.Expect100Continue = false;
                point.UseNagleAlgorithm = false;
            }
            catch (PlatformNotSupportedException)
            {
                // .NET Core: neither applies
            }
            _http = new HttpClient(handler ?? new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }, disposeHandler: true)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
                MaxResponseContentBufferSize = 4 * 1024 * 1024,
            };
            _http.DefaultRequestHeaders.ExpectContinue = false;
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        public Uri BaseUri => _base;

        /// <summary>The session the touches go to; null before <see cref="StartSessionAsync"/>.</summary>
        public string SessionId { get; private set; }

        /// <summary>GET /status: true when WDA answers and says it is ready. Never throws for a failed request.</summary>
        public async Task<bool> IsReadyAsync(TimeSpan timeout, CancellationToken token)
        {
            try
            {
                var (status, body) = await SendAsync(HttpMethod.Get, "/status", null, timeout, token).ConfigureAwait(false);
                return status == 200 && WdaResponses.IsReady(body);
            }
            catch (WdaException)
            {
                return false;
            }
        }

        /// <summary>A new session that never waits for the app to go idle.</summary>
        public async Task StartSessionAsync(CancellationToken token)
        {
            var (status, body) = await SendAsync(HttpMethod.Post, "/session", WdaRequests.NewSession(), RequestTimeout, token).ConfigureAwait(false);
            Check("POST /session", status, body);
            var id = WdaResponses.SessionId(body);
            if (id == null || !IsPathSafe(id)) throw new WdaException("WDA gave no session id", status);
            SessionId = id;
            try
            {
                // gestures go out at once: no waiting for animations or for the app to go idle
                var (s, b) = await SendAsync(HttpMethod.Post, SessionPath("/appium/settings"), WdaRequests.Settings(), RequestTimeout, token).ConfigureAwait(false);
                if (s != 200) Logged?.Invoke("settings refused (HTTP " + s + ")");
            }
            catch (WdaException e) when (!e.NoAnswer)
            {
                // an older build without these settings still works
            }
        }

        /// <summary>Raised with a short line (paths and status codes only) for the log.</summary>
        public event Action<string> Logged;

        public async Task<DeviceSize> WindowSizeAsync(CancellationToken token)
        {
            var body = await SessionAsync(HttpMethod.Get, "/window/size", null, RequestTimeout, token).ConfigureAwait(false);
            var size = WdaResponses.WindowSize(body);
            if (!size.IsKnown) throw new WdaException("WDA gave no screen size", 200);
            return size;
        }

        public Task TouchAsync(IReadOnlyList<TouchStep> steps, CancellationToken token) =>
            SessionAsync(HttpMethod.Post, "/actions", WdaRequests.Actions(steps), ActionTimeout, token);

        public Task TypeAsync(string text, CancellationToken token) =>
            SessionAsync(HttpMethod.Post, "/wda/keys", WdaRequests.Keys(text), RequestTimeout, token);

        public Task PressButtonAsync(string name, CancellationToken token) =>
            SessionAsync(HttpMethod.Post, "/wda/pressButton", WdaRequests.Button(name), RequestTimeout, token);

        public Task HidHomeAsync(CancellationToken token) =>
            SessionAsync(HttpMethod.Post, "/wda/performIoHidEvent", WdaRequests.HidHome(), RequestTimeout, token);

        public async Task HomeAsync(CancellationToken token)
        {
            var (status, body) = await SendAsync(HttpMethod.Post, "/wda/homescreen", WdaRequests.Empty, RequestTimeout, token).ConfigureAwait(false);
            Check("POST /wda/homescreen", status, body);
        }

        public void Dispose() => _http.Dispose();

        /// <summary>A request on the session, with a new session once if WDA forgot this one.</summary>
        private async Task<string> SessionAsync(HttpMethod method, string path, string body, TimeSpan timeout, CancellationToken token)
        {
            if (SessionId == null) await StartSessionAsync(token).ConfigureAwait(false);
            var (status, answer) = await SendAsync(method, SessionPath(path), body, timeout, token).ConfigureAwait(false);
            if (WdaResponses.IsInvalidSession(status, answer))
            {
                Logged?.Invoke("session gone, starting a new one");
                await StartSessionAsync(token).ConfigureAwait(false);
                (status, answer) = await SendAsync(method, SessionPath(path), body, timeout, token).ConfigureAwait(false);
            }
            Check(method.Method + " " + path, status, answer);
            return answer;
        }

        private string SessionPath(string path) => "/session/" + SessionId + path;

        private static void Check(string what, int status, string body)
        {
            if (status >= 400 || WdaResponses.IsLegacyFailure(status, body))
            {
                var error = WdaResponses.Error(body);
                throw new WdaException(what + " -> HTTP " + status + (error != null ? " " + error : ""), status, error);
            }
        }

        private async Task<(int Status, string Body)> SendAsync(HttpMethod method, string path, string body, TimeSpan timeout, CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var request = new HttpRequestMessage(method, new Uri(_base, path)))
            {
                deadline.CancelAfter(timeout);
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token).ConfigureAwait(false))
                    {
                        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return ((int)response.StatusCode, text);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new WdaException(method.Method + " " + path + " timed out");
                }
                catch (HttpRequestException e)
                {
                    throw new WdaException(method.Method + " " + path + " failed: " + (e.InnerException?.GetType().Name ?? e.GetType().Name));
                }
                catch (System.IO.IOException e)
                {
                    throw new WdaException(method.Method + " " + path + " failed: " + e.GetType().Name);
                }
            }
        }

        /// <summary>A session id goes into the path: WDA's are UUIDs.</summary>
        private static bool IsPathSafe(string id)
        {
            foreach (var c in id)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-')) return false;
            }
            return id.Length <= 64;
        }
    }
}

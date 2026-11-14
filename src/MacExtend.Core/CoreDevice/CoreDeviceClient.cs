using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MacExtend.Core.CoreDevice
{
    public enum ReleaseOutcome
    {
        NothingHeld,
        Released,

        /// <summary>A release was not answered in time; the device may still see a finger or a key down.</summary>
        Unconfirmed,
    }

    /// <summary>The device's buttons serve-web's /button presses (it also knows lock, mute and siri, which nothing here sends).</summary>
    public enum DeviceButton
    {
        Home,
        VolumeUp,
        VolumeDown,
    }

    public sealed class CoreDeviceClientException : Exception
    {
        public CoreDeviceClientException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Posts input to serve-web's /touch, /key and /button on loopback. serve-web answers 200 as soon as a request is
    /// queued, so a 200 means "accepted", not "the device saw it". Requests go one at a time in call order and are
    /// never retried; a contact or key is marked possibly held before it is sent, because a request that times out
    /// may still have been queued. After any failure the client takes no new input; <see cref="ReleaseAllAsync"/>
    /// still runs, on its own deadline.
    /// </summary>
    public sealed class CoreDeviceClient : IDisposable
    {
        public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(2);

        public const int MaxCoordinate = 65535;
        public const int CenterCoordinate = 32768;

        // HID keyboard page: 0x04 (a) .. 0xE7 (right GUI); serve-web's report cannot carry usages from 240 up
        public const int MinUsage = 0x04;
        public const int MaxUsage = 0xE7;

        private readonly HttpClient _http;
        private readonly TimeSpan _requestTimeout;
        private readonly object _lock = new object();
        private Task _tail = Task.CompletedTask;
        private bool _open = true;
        private bool _contactHeld;
        private bool _keysHeld;
        private int _lastX = CenterCoordinate;
        private int _lastY = CenterCoordinate;

        public CoreDeviceClient(int port) : this(port, CreateHandler(), DefaultRequestTimeout)
        {
        }

        internal CoreDeviceClient(int port, HttpMessageHandler handler, TimeSpan requestTimeout)
        {
            if (port < CoreDeviceOptions.MinHttpPort || port > CoreDeviceOptions.MaxHttpPort) throw new ArgumentOutOfRangeException(nameof(port));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _requestTimeout = requestTimeout;
            _http = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = new Uri($"http://{CoreDeviceArguments.BindAddress}:{port.ToString(CultureInfo.InvariantCulture)}/"),
                // each request gets its own timeout below
                Timeout = Timeout.InfiniteTimeSpan,
            };
            // .NET Framework otherwise sends "Expect: 100-continue" and holds the body back; serve-web never answers 100
            _http.DefaultRequestHeaders.ExpectContinue = false;
        }

        /// <summary>Loopback only: no proxy, no redirects, no cookies.</summary>
        public static HttpClientHandler CreateHandler() => new HttpClientHandler
        {
            UseProxy = false,
            Proxy = null,
            AllowAutoRedirect = false,
            UseCookies = false,
        };

        public bool IsOpen
        {
            get { lock (_lock) return _open; }
        }

        public bool ContactMayBeHeld
        {
            get { lock (_lock) return _contactHeld; }
        }

        public bool KeysMayBeHeld
        {
            get { lock (_lock) return _keysHeld; }
        }

        /// <summary>A finger down at (x, y), or moved there if already down. HID coordinates, 0..65535, portrait.</summary>
        public Task ContactAsync(int x, int y, CancellationToken token)
        {
            CheckCoordinates(x, y);
            return SendAsync("touch", Touch("contact", x, y), cleanup: false, token, before: () =>
            {
                _contactHeld = true;
                _lastX = x;
                _lastY = y;
            }, after: null);
        }

        public Task ReleaseContactAsync(int x, int y, CancellationToken token)
        {
            CheckCoordinates(x, y);
            return SendAsync("touch", Touch("release", x, y), cleanup: false, token, before: () =>
            {
                _lastX = x;
                _lastY = y;
            }, after: () => _contactHeld = false);
        }

        /// <summary>serve-web's own tap: contact, 50 ms, release, run by its HID worker in one go.</summary>
        public Task TapAsync(int x, int y, CancellationToken token)
        {
            CheckCoordinates(x, y);
            return SendAsync("touch", Touch("tap", x, y), cleanup: false, token, before: null, after: null);
        }

        /// <summary>The full set of keyboard usages held down; empty releases every key.</summary>
        public Task SetKeysAsync(IEnumerable<int> usages, CancellationToken token)
        {
            if (usages == null) throw new ArgumentNullException(nameof(usages));
            var set = usages.ToList();
            foreach (var usage in set)
            {
                if (usage < MinUsage || usage > MaxUsage) throw new ArgumentOutOfRangeException(nameof(usages), $"keyboard usages run from {MinUsage} to {MaxUsage}");
            }
            if (set.Distinct().Count() != set.Count) throw new ArgumentException("a usage is listed twice", nameof(usages));
            set.Sort();
            var held = set.Count > 0;
            return SendAsync("key", Keys(set), cleanup: false, token, before: () => _keysHeld |= held, after: () => _keysHeld = held);
        }

        public Task HomeAsync(CancellationToken token) => PressAsync(DeviceButton.Home, token);

        /// <summary>A press: serve-web holds the button down for as long as that button needs, then lets it go.</summary>
        public Task PressAsync(DeviceButton button, CancellationToken token)
        {
            string name;
            switch (button)
            {
                case DeviceButton.Home: name = "home"; break;
                case DeviceButton.VolumeUp: name = "volume-up"; break;
                case DeviceButton.VolumeDown: name = "volume-down"; break;
                default: throw new ArgumentOutOfRangeException(nameof(button));
            }
            return SendAsync("button", "{\"name\":\"" + name + "\",\"state\":\"press\"}", cleanup: false, token, before: null, after: null);
        }

        /// <summary>Takes no new input from now on. Queued requests still run; cleanup is still allowed.</summary>
        public void Close()
        {
            lock (_lock) _open = false;
        }

        /// <summary>
        /// Lifts the last contact and releases every key if either may be held (both when <paramref name="force"/>),
        /// within <paramref name="deadline"/>, whatever happened to earlier requests. Whatever the outcome, the marks
        /// are cleared afterwards: nothing is replayed later.
        /// </summary>
        public async Task<ReleaseOutcome> ReleaseAllAsync(TimeSpan deadline, bool force = false)
        {
            bool contact, keys;
            int x, y;
            lock (_lock)
            {
                contact = force || _contactHeld;
                keys = force || _keysHeld;
                x = _lastX;
                y = _lastY;
            }
            if (!contact && !keys) return ReleaseOutcome.NothingHeld;

            using (var cts = new CancellationTokenSource(deadline))
            {
                try
                {
                    // each one on its own: a failed contact release must not keep the keys down
                    var released = true;
                    if (contact) released &= await CleanupAsync("touch", Touch("release", x, y), cts.Token).ConfigureAwait(false);
                    if (keys) released &= await CleanupAsync("key", Keys(new List<int>()), cts.Token).ConfigureAwait(false);
                    return released ? ReleaseOutcome.Released : ReleaseOutcome.Unconfirmed;
                }
                finally
                {
                    lock (_lock)
                    {
                        _contactHeld = false;
                        _keysHeld = false;
                    }
                }
            }
        }

        public void Dispose()
        {
            Close();
            _http.Dispose();
        }

        private Task SendAsync(string path, string json, bool cleanup, CancellationToken token, Action before, Action after)
        {
            lock (_lock)
            {
                if (!cleanup && !_open) return Task.FromException(Closed());
                var previous = _tail;
                var task = RunAsync(previous, path, json, cleanup, token, before, after);
                // the next request waits for this one and for everything before it, even if this one gave up waiting
                _tail = Task.WhenAll(previous, task.ContinueWith(_ => { }, TaskScheduler.Default));
                return task;
            }
        }

        private async Task RunAsync(Task previous, string path, string json, bool cleanup, CancellationToken token, Action before, Action after)
        {
            await WaitTurnAsync(previous, token).ConfigureAwait(false);
            lock (_lock)
            {
                if (!cleanup && !_open) throw Closed();
                before?.Invoke();
            }

            using (var timeout = new CancellationTokenSource(_requestTimeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token))
            using (var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") })
            {
                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is OperationCanceledException || e is HttpRequestException)
                {
                    // the request may or may not have reached serve-web's queue: what it marked stays marked
                    Fail();
                    if (token.IsCancellationRequested) throw new OperationCanceledException($"/{path} was cancelled while in flight", e, token);
                    if (timeout.IsCancellationRequested) throw new CoreDeviceClientException($"/{path} timed out after {_requestTimeout.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms", e);
                    throw new CoreDeviceClientException($"/{path} failed: {e.GetBaseException().Message}", e);
                }
                using (response)
                {
                    if ((int)response.StatusCode != 200)
                    {
                        Fail();
                        throw new CoreDeviceClientException($"/{path} answered HTTP {(int)response.StatusCode}");
                    }
                }
            }
            if (after != null)
            {
                lock (_lock) after();
            }
        }

        private async Task<bool> CleanupAsync(string path, string json, CancellationToken token)
        {
            try
            {
                await SendAsync(path, json, cleanup: true, token, before: null, after: null).ConfigureAwait(false);
                return true;
            }
            catch (Exception e) when (e is CoreDeviceClientException || e is OperationCanceledException)
            {
                return false;
            }
        }

        private static async Task WaitTurnAsync(Task previous, CancellationToken token)
        {
            if (!previous.IsCompleted)
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (token.Register(() => cancelled.TrySetResult(true)))
                {
                    await Task.WhenAny(previous, cancelled.Task).ConfigureAwait(false);
                }
            }
            token.ThrowIfCancellationRequested();
        }

        private void Fail()
        {
            lock (_lock) _open = false;
        }

        private static CoreDeviceClientException Closed() =>
            new CoreDeviceClientException("the input session is closed after an earlier failure or a stop; start a new serve-web session");

        internal static void CheckCoordinates(int x, int y)
        {
            if (x < 0 || x > MaxCoordinate) throw new ArgumentOutOfRangeException(nameof(x), "HID coordinates run from 0 to 65535");
            if (y < 0 || y > MaxCoordinate) throw new ArgumentOutOfRangeException(nameof(y), "HID coordinates run from 0 to 65535");
        }

        private static string Touch(string type, int x, int y) =>
            "{\"type\":\"" + type + "\",\"x\":" + x.ToString(CultureInfo.InvariantCulture) + ",\"y\":" + y.ToString(CultureInfo.InvariantCulture) + "}";

        private static string Keys(List<int> usages) =>
            "{\"usages\":[" + string.Join(",", usages.Select(u => u.ToString(CultureInfo.InvariantCulture))) + "]}";
    }
}

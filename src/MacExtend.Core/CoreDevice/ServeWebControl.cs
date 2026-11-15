using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// The calls the window's toolbar makes besides the viewer: Home and the device clipboard. Unlike CoreDeviceClient
    /// nothing here holds a finger or a key down, so a failed call closes nothing; the next one is tried again.
    /// </summary>
    /// <summary>What direct_input.py's /warm answered.</summary>
    public enum WarmResult
    {
        /// <summary>The media stream is open: input goes through at once.</summary>
        Open,
        /// <summary>The device's camera or microphone is in use: no input until that app lets go.</summary>
        MediaBusy,
        /// <summary>The stream did not open for another reason, or no answer came; the next input tries again.</summary>
        Failed,
    }

    public sealed class ServeWebControl : IDisposable
    {
        private readonly HttpClient _http;
        private readonly TimeSpan _timeout;

        /// <summary>Each call's limit unless the caller chooses one: enough for a button.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        /// <summary>/clipboard goes to the device and back, which over Wi-Fi can take longer than a button.</summary>
        public static readonly TimeSpan ClipboardTimeout = TimeSpan.FromSeconds(15);

        /// <summary>/warm answers once the media stream opened, which direct_input.py bounds at 15 s.</summary>
        public static readonly TimeSpan WarmTimeout = TimeSpan.FromSeconds(20);

        public ServeWebControl(int port) : this(port, DefaultTimeout)
        {
        }

        /// <param name="timeout">How long each call may take before it fails with <see cref="CoreDeviceClientException"/>.</param>
        public ServeWebControl(int port, TimeSpan timeout) : this(port, CoreDeviceClient.CreateHandler(), timeout)
        {
        }

        internal ServeWebControl(int port, HttpMessageHandler handler, TimeSpan timeout)
        {
            if (port < CoreDeviceOptions.MinHttpPort || port > CoreDeviceOptions.MaxHttpPort) throw new ArgumentOutOfRangeException(nameof(port));
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            _timeout = timeout;
            _http = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = new Uri($"http://{CoreDeviceArguments.BindAddress}:{port.ToString(CultureInfo.InvariantCulture)}/"),
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _http.DefaultRequestHeaders.ExpectContinue = false;
        }

        public Task HomeAsync(CancellationToken token) => SendAsync(HttpMethod.Post, "button", "{\"name\":\"home\",\"state\":\"press\"}", token);

        /// <summary>direct_input.py opens its media stream now. Up to <see cref="WarmTimeout"/> when the control was made with it.</summary>
        public async Task<WarmResult> WarmAsync(CancellationToken token)
        {
            try
            {
                await SendAsync(HttpMethod.Post, "warm", null, token).ConfigureAwait(false);
                return WarmResult.Open;
            }
            catch (CoreDeviceClientException e)
            {
                return e.MediaBusy ? WarmResult.MediaBusy : WarmResult.Failed;
            }
        }

        public Task SendTextAsync(string text, CancellationToken token) => SendAsync(HttpMethod.Post, "clipboard", "{\"text\":" + JsonString(text ?? "") + "}", token);

        /// <returns>The text on the device clipboard, or null when it holds none (an image, or nothing).</returns>
        public async Task<string> GetTextAsync(CancellationToken token)
        {
            var body = await SendAsync(HttpMethod.Get, "clipboard", null, token).ConfigureAwait(false);
            if (!MiniJson.TryParse(body, out var value) || !(value is Dictionary<string, object> map)) throw new CoreDeviceClientException("/clipboard answered something that is not JSON");
            return map.TryGetValue("text", out var text) ? text as string : null;
        }

        public void Dispose() => _http.Dispose();

        private async Task<string> SendAsync(HttpMethod method, string path, string json, CancellationToken token)
        {
            using (var timeout = new CancellationTokenSource(_timeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token))
            using (var request = new HttpRequestMessage(method, path))
            {
                if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token).ConfigureAwait(false))
                    {
                        if ((int)response.StatusCode != 200)
                        {
                            throw new CoreDeviceClientException($"/{path} answered HTTP {(int)response.StatusCode}", null,
                                                                (int)response.StatusCode == CoreDeviceClient.MediaBusyStatus);
                        }
                        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception e) when (e is OperationCanceledException || e is HttpRequestException)
                {
                    if (token.IsCancellationRequested) throw;
                    throw new CoreDeviceClientException($"/{path} failed: {e.GetBaseException().Message}", e);
                }
            }
        }

        internal static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}

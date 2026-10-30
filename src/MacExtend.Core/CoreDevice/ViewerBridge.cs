using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// What the control window puts into serve-web's viewer page, and the one page it lets the WebView2 show.
    /// Element ids, classes and localStorage keys are pymobiledevice3 11.47.0's resources/serve_web/viewer.html and
    /// viewer.js: #topbar, #left-tray, #right-tray, #bottom-row, #hw-tooltip, #help-overlay, .hw, #stage, #stage-wrap,
    /// #workspace, #device-frame, #c (the canvas; keys go to the device only while it has focus), #offline-overlay span,
    /// #rotate-left, #rotate-right; frameOn, zoom, tray-left, tray-right, clipboardSync.
    /// </summary>
    public static class ViewerBridge
    {
        /// <summary>The viewer's own label over a picture that stopped ("Stream offline — waiting for frames…").</summary>
        public const string OfflineText = "Đang chờ hình. Nếu màn hình thiết bị đang tắt, hãy mở khóa thiết bị.";

        public const string FocusCanvas = "(function () { var c = document.getElementById('c'); if (c) c.focus(); })();";

        /// <summary>
        /// The viewer's hidden rotate buttons: viewer.js turns its picture itself (setVisualRotation) before POST /rotate,
        /// so the window clicks them instead of calling /rotate.
        /// </summary>
        public const string RotateLeft = "(function () { var b = document.getElementById('rotate-left'); if (b) b.click(); })();";

        public const string RotateRight = "(function () { var b = document.getElementById('rotate-right'); if (b) b.click(); })();";

        private static readonly Regex HexColour = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

        // Everything but the canvas and its offline overlay goes. viewer.js sizes the canvas inline and keeps 88 x 150 px
        // around it for that chrome; the size here overrides it so the picture fills the window, its proportions kept.
        private const string Css =
            "#topbar,#left-tray,#right-tray,#bottom-row,#hw-tooltip,#help-overlay,.hw{display:none!important}"
            + "html,body{height:100%;overflow:hidden}"
            + "body{padding:0!important;gap:0!important;background:{BG}!important}"
            + "#workspace{align-items:center!important;justify-content:center!important}"
            + "#stage::before{display:none!important}"
            + "#stage-wrap{padding:0!important}"
            + "#c{width:auto!important;height:auto!important;max-width:calc(100vw - 16px);max-height:calc(100vh - 16px);border-radius:10px;cursor:default}"
            + "#offline-overlay{border-radius:10px}"
            + "#offline-overlay span{white-space:normal!important;text-align:center;max-width:80%;line-height:1.45}";

        /// <summary>
        /// Runs in the viewer page before its own script (AddScriptToExecuteOnDocumentCreatedAsync): no bezel, no zoom,
        /// no clipboard sync, only the canvas on <paramref name="background"/>, and the offline label in Vietnamese.
        /// The keys viewer.js keeps for itself are masked so they reach the device like any other key: '?' (its help,
        /// hidden here, would swallow every key until Esc), Ctrl+P (a screenshot download), Ctrl+= / Ctrl+- / Ctrl+0 (zoom).
        /// </summary>
        /// <param name="background">The window's stage colour as #RRGGBB.</param>
        /// <exception cref="ArgumentException">Not #RRGGBB.</exception>
        public static string DocumentScript(string background)
        {
            if (background == null || !HexColour.IsMatch(background)) throw new ArgumentException("the background must be #RRGGBB", nameof(background));
            return "(function () {\n"
                + "  if (location.protocol !== 'http:') return;\n"
                + "  try {\n"
                + "    localStorage.setItem('frameOn', 'false');\n"
                + "    localStorage.removeItem('zoom');\n"
                + "    localStorage.setItem('tray-left', 'collapsed');\n"
                + "    localStorage.setItem('tray-right', 'collapsed');\n"
                + "    localStorage.setItem('clipboardSync', 'false');\n"
                + "  } catch (e) {}\n"
                + "  window.addEventListener('keydown', function (e) {\n"
                + "    if (e.altKey || e.metaKey || typeof e.key !== 'string' || e.key.length !== 1) return;\n"
                + "    if ((e.key === '?' && !e.ctrlKey) || (e.ctrlKey && 'p=+-0'.indexOf(e.key.toLowerCase()) >= 0)) {\n"
                + "      try { Object.defineProperty(e, 'key', { value: 'Unidentified' }); } catch (x) {}\n"
                + "    }\n"
                + "  }, true);\n"
                + "  document.addEventListener('DOMContentLoaded', function () {\n"
                + "    var style = document.createElement('style');\n"
                + "    style.textContent = " + JsString(Css.Replace("{BG}", background)) + ";\n"
                + "    document.head.appendChild(style);\n"
                + "    document.body.classList.remove('frame-on');\n"
                + "    var span = document.querySelector('#offline-overlay span');\n"
                + "    if (span) span.textContent = " + JsString(OfflineText) + ";\n"
                + "    window.dispatchEvent(new Event('resize'));\n"
                + "  });\n"
                + "})();";
        }

        /// <summary>
        /// The only pages the window shows: the session's viewer at http://127.0.0.1:<paramref name="viewerPort"/>/ while
        /// one is loaded, and the blank page only between sessions (<paramref name="viewerPort"/> 0), so history cannot
        /// lead from a viewer back to a black page. Any other address, path, query or scheme is blocked.
        /// </summary>
        /// <param name="viewerPort">serve-web's port, 0 while no viewer is loaded.</param>
        public static bool IsAllowedNavigation(string uri, int viewerPort)
        {
            if (uri == "about:blank") return viewerPort == 0;
            if (viewerPort < CoreDeviceOptions.MinHttpPort || viewerPort > CoreDeviceOptions.MaxHttpPort) return false;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
            return u.Scheme == Uri.UriSchemeHttp
                && u.HostNameType == UriHostNameType.IPv4 && u.Host == CoreDeviceArguments.BindAddress
                && u.Port == viewerPort
                && u.UserInfo.Length == 0
                && u.AbsolutePath == "/"
                && u.Query.Length == 0;
        }

        /// <summary>A JavaScript string literal in single quotes; everything outside printable ASCII is a \u escape.</summary>
        public static string JsString(string text)
        {
            var sb = new StringBuilder("'");
            foreach (var c in text ?? "")
            {
                switch (c)
                {
                    case '\'': sb.Append("\\'"); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default:
                        if (c < ' ' || c > '~' || c == '<' || c == '>') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('\'').ToString();
        }
    }
}

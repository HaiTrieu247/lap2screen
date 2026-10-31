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
    /// #rotate-left, #rotate-right; frameOn, zoom, tray-left, tray-right, clipboardSync. The wheel also uses viewer.js's
    /// globals touchCoords and activePointer, and its POST /touch {type: contact|release, x, y}.
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

        // viewer.js configures its HEVC decoder with codec and hvcC only. Edge's decoder (WebView2's too) then decodes the
        // whole picture (coded 832x1808) but marks only the part inside WebCodecs' default 1280x720 visible and shows it
        // as 1280x720, so the viewer drew the top 40 % stretched sideways; Chrome takes the size from the stream. Every
        // frame cut short by more than a conformance window can (less than one 64 px coding tree block) is handed on as
        // the whole decoded picture, so the canvas, the rotation and the collapse compensation all see the real size.
        private const string WholePictureDecoder =
            "  if (window.VideoDecoder && window.VideoFrame) {\n"
            + "    var wholePicture = function (frame) {\n"
            + "      var r = frame.visibleRect, w = frame.codedWidth, h = frame.codedHeight;\n"
            + "      if (!r || (w - r.width < 64 && h - r.height < 64)) return frame;\n"
            + "      try {\n"
            + "        var whole = new VideoFrame(frame, { visibleRect: { x: 0, y: 0, width: w, height: h }, displayWidth: w, displayHeight: h });\n"
            + "        frame.close();\n"
            + "        return whole;\n"
            + "      } catch (e) { return frame; }\n"
            + "    };\n"
            + "    var NativeDecoder = window.VideoDecoder;\n"
            + "    var Decoder = function (init) {\n"
            + "      var output = init.output;\n"
            + "      return new NativeDecoder({ output: function (frame) { output(wholePicture(frame)); }, error: init.error });\n"
            + "    };\n"
            + "    Decoder.isConfigSupported = function (config) { return NativeDecoder.isConfigSupported(config); };\n"
            + "    Decoder.prototype = NativeDecoder.prototype;\n"
            + "    window.VideoDecoder = Decoder;\n"
            + "  }\n";

        // viewer.js turns only the left button into a finger. The wheel and two-finger touchpad scrolling become a finger
        // drag through the same /touch and touchCoords (a rotated picture scrolls the way it looks), one finger for a whole
        // scroll: it stays down until nothing has come for 400 ms. Edge gives 33.33 px per Windows line ("Lines to scroll
        // at a time"), so a line becomes a 22 pt line of iOS text, a touchpad at the same rate; the device's points come
        // from the picture's size. A wheel notch (a multiple of 120 in wheelDelta) is eased out over a few frames; a
        // touchpad already moves smoothly and glides by itself (Windows), so the finger follows it as it comes. Every touch
        // first moves the 10 pt iOS needs before it scrolls, so the content moves by all that was asked and no touch is a
        // tap. It goes down away from the edges where iOS starts Home, Control Center and Back, may then travel to 3 % of
        // them, and is put down again on the far side to go on. Never while the left button holds the viewer's own finger;
        // any press, blur or hidden page lifts it. One request at a time, a move that has not gone yet replaced by the
        // newer one, so a slow Wi-Fi tunnel lags no further behind and a release never overtakes the moves before it.
        private const string WheelDrag =
            "  var wheelDrag = function (canvas) {\n"
            + "    var finger = null, target = null, carry = null, origin = null, smooth = false, lastWheel = 0, timer = 0;\n"
            + "    var queue = [], busy = false;\n"
            + "    var pump = function () {\n"
            + "      if (busy || !queue.length) return;\n"
            + "      busy = true;\n"
            + "      var t = queue.shift();\n"
            + "      fetch('/touch', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ type: t.type, x: t.x, y: t.y }) })\n"
            + "        .catch(function () {})\n"
            + "        .then(function () { busy = false; pump(); });\n"
            + "    };\n"
            + "    var send = function (type, p, first) {\n"
            + "      var c = touchCoords({ clientX: p.x, clientY: p.y }), last = queue[queue.length - 1];\n"
            + "      var t = { type: type, x: c.x, y: c.y, first: !!first };\n"
            + "      if (type === 'contact' && !first && last && last.type === 'contact' && !last.first) queue[queue.length - 1] = t;\n"
            + "      else queue.push(t);\n"
            + "      pump();\n"
            + "    };\n"
            + "    var within = function (p, fx, fy) {\n"
            + "      var r = canvas.getBoundingClientRect();\n"
            + "      return { x: Math.min(r.right - r.width * fx, Math.max(r.left + r.width * fx, p.x)),\n"
            + "               y: Math.min(r.bottom - r.height * fy, Math.max(r.top + r.height * fy, p.y)) };\n"
            + "    };\n"
            + "    var pxPerPoint = function () {\n"
            + "      var l = Math.max(canvas.width, canvas.height), s = Math.min(canvas.width, canvas.height);\n"
            + "      return canvas.getBoundingClientRect().height * (l / s > 1.9 && l > 2000 ? 3 : 2) / canvas.height;\n"
            + "    };\n"
            + "    var aim = function (move) {\n"
            + "      var want = { x: target.x + carry.x + move.x, y: target.y + carry.y + move.y };\n"
            + "      target = within(want, 0.03, 0.03);\n"
            + "      carry = { x: want.x - target.x, y: want.y - target.y };\n"
            + "    };\n"
            + "    var down = function (start, move) {\n"
            + "      finger = target = start;\n"
            + "      carry = { x: 0, y: 0 };\n"
            + "      send('contact', finger, true);\n"
            + "      var slop = 10 * pxPerPoint(), vertical = Math.abs(move.y) >= Math.abs(move.x);\n"
            + "      target = within({ x: start.x + (vertical ? 0 : Math.sign(move.x) * slop), y: start.y + (vertical ? Math.sign(move.y) * slop : 0) }, 0.03, 0.03);\n"
            + "      aim(move);\n"
            + "    };\n"
            + "    var up = function () {\n"
            + "      if (!finger) return;\n"
            + "      send('release', finger);\n"
            + "      finger = target = carry = null;\n"
            + "    };\n"
            + "    var toward = function (from, to) {\n"
            + "      var d = to - from;\n"
            + "      return Math.abs(d) <= 1 ? to : from + d * 0.22;\n"
            + "    };\n"
            + "    var schedule = function () { if (!timer) timer = setTimeout(tick, 16); };\n"
            + "    var tick = function () {\n"
            + "      timer = 0;\n"
            + "      if (!finger) return;\n"
            + "      if (finger.x !== target.x || finger.y !== target.y) {\n"
            + "        finger = smooth ? { x: toward(finger.x, target.x), y: toward(finger.y, target.y) } : target;\n"
            + "        send('contact', finger);\n"
            + "      } else if (carry.x || carry.y) {\n"
            + "        var r = canvas.getBoundingClientRect(), rest = carry;\n"
            + "        up();\n"
            + "        down(within({ x: rest.x > 0 ? r.left : rest.x < 0 ? r.right : origin.x, y: rest.y > 0 ? r.top : rest.y < 0 ? r.bottom : origin.y }, 0.15, 0.2), rest);\n"
            + "      } else if (Date.now() - lastWheel >= 400) {\n"
            + "        up();\n"
            + "        return;\n"
            + "      }\n"
            + "      schedule();\n"
            + "    };\n"
            + "    var cancel = function () {\n"
            + "      if (timer) { clearTimeout(timer); timer = 0; }\n"
            + "      up();\n"
            + "    };\n"
            + "    window.addEventListener('wheel', function (e) {\n"
            + "      if (e.ctrlKey) return;\n"
            + "      e.preventDefault();\n"
            + "      if (!canvas.width || !canvas.height || (typeof activePointer !== 'undefined' && activePointer !== null)) return;\n"
            + "      var dx = e.deltaX, dy = e.deltaY;\n"
            + "      if (e.deltaMode === 1) { dx *= 100 / 3; dy *= 100 / 3; }\n"
            + "      if (e.shiftKey && !dx) { dx = dy; dy = 0; }\n"
            + "      if (!dx && !dy) return;\n"
            + "      var r = canvas.getBoundingClientRect(), page = e.deltaMode === 2, k = pxPerPoint() * 0.66;\n"
            + "      var kx = page ? r.width * 0.875 : k, ky = page ? r.height * 0.875 : k;\n"
            + "      var move = { x: -dx * kx, y: -dy * ky };\n"
            + "      smooth = (e.wheelDeltaY && e.wheelDeltaY % 120 === 0) || (e.wheelDeltaX && e.wheelDeltaX % 120 === 0);\n"
            + "      lastWheel = Date.now();\n"
            + "      if (finger) aim(move);\n"
            + "      else {\n"
            + "        origin = within({ x: e.clientX, y: e.clientY }, 0.15, 0.2);\n"
            + "        down(origin, move);\n"
            + "      }\n"
            + "      schedule();\n"
            + "    }, { passive: false });\n"
            + "    window.addEventListener('pointerdown', cancel, true);\n"
            + "    window.addEventListener('blur', cancel);\n"
            + "    document.addEventListener('visibilitychange', cancel);\n"
            + "  };\n";

        /// <summary>
        /// Runs in the viewer page before its own script (AddScriptToExecuteOnDocumentCreatedAsync): no bezel, no zoom,
        /// no clipboard sync, only the canvas on <paramref name="background"/>, the whole decoded picture even where the
        /// decoder marks only part of it visible (Edge), the wheel as a finger drag, and the offline label in Vietnamese.
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
                + WholePictureDecoder
                + WheelDrag
                + "  document.addEventListener('DOMContentLoaded', function () {\n"
                + "    var style = document.createElement('style');\n"
                + "    style.textContent = " + JsString(Css.Replace("{BG}", background)) + ";\n"
                + "    document.head.appendChild(style);\n"
                + "    document.body.classList.remove('frame-on');\n"
                + "    var span = document.querySelector('#offline-overlay span');\n"
                + "    if (span) span.textContent = " + JsString(OfflineText) + ";\n"
                + "    var c = document.getElementById('c');\n"
                + "    if (c && typeof touchCoords === 'function') wheelDrag(c);\n"
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

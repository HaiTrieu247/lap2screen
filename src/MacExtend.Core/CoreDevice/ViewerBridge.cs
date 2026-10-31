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
        // drag through the same /touch and touchCoords (a rotated picture scrolls the way it looks): a notch moves the
        // finger a tenth of the picture, notches in a row move the finger already down, and a wheel that was turning fast
        // flicks on release so iOS scrolls on by itself. The finger stays in the middle of the picture, away from the
        // edges where iOS starts Home, Control Center and Back, and is put down again on the far side when it reaches the
        // edge of that band. It goes down only once the wheel asks for more than a tap, never while the left button holds
        // the viewer's own finger, and lets go on any press, blur or hidden page. Its requests go one after another, so a
        // release never overtakes the moves before it.
        private const string WheelDrag =
            "  var wheelDrag = function (canvas) {\n"
            + "    var finger = null, target = null, carry = null, dir = { x: 0, y: 0 };\n"
            + "    var origin = null, pending = { x: 0, y: 0 }, recent = [], lastWheel = 0, timer = 0, queue = Promise.resolve();\n"
            + "    var send = function (type, p) {\n"
            + "      var c = touchCoords({ clientX: p.x, clientY: p.y });\n"
            + "      queue = queue.then(function () {\n"
            + "        return fetch('/touch', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ type: type, x: c.x, y: c.y }) });\n"
            + "      }).catch(function () {});\n"
            + "    };\n"
            + "    var inside = function (p) {\n"
            + "      var r = canvas.getBoundingClientRect();\n"
            + "      return { x: Math.min(r.right - r.width * 0.1, Math.max(r.left + r.width * 0.1, p.x)),\n"
            + "               y: Math.min(r.bottom - r.height * 0.2, Math.max(r.top + r.height * 0.2, p.y)) };\n"
            + "    };\n"
            + "    var aim = function (move) {\n"
            + "      var want = { x: target.x + carry.x + move.x, y: target.y + carry.y + move.y };\n"
            + "      target = inside(want);\n"
            + "      carry = { x: want.x - target.x, y: want.y - target.y };\n"
            + "      dir = { x: move.x ? Math.sign(move.x) : dir.x, y: move.y ? Math.sign(move.y) : dir.y };\n"
            + "    };\n"
            + "    var down = function (start, move) {\n"
            + "      finger = target = inside(start);\n"
            + "      carry = { x: 0, y: 0 };\n"
            + "      dir = { x: 0, y: 0 };\n"
            + "      aim(move);\n"
            + "      send('contact', finger);\n"
            + "      schedule();\n"
            + "    };\n"
            + "    var up = function (flick) {\n"
            + "      if (!finger) return;\n"
            + "      if (flick) {\n"
            + "        var r = canvas.getBoundingClientRect();\n"
            + "        finger = { x: finger.x + dir.x * r.width * 0.06, y: finger.y + dir.y * r.height * 0.06 };\n"
            + "        send('contact', finger);\n"
            + "      }\n"
            + "      send('release', finger);\n"
            + "      finger = target = carry = null;\n"
            + "    };\n"
            + "    var toward = function (from, to) {\n"
            + "      var d = to - from;\n"
            + "      return Math.abs(d) <= 1 ? to : from + d * 0.35 + (d > 0 ? 0.5 : -0.5);\n"
            + "    };\n"
            + "    var fast = function () {\n"
            + "      var sum = 0;\n"
            + "      recent.forEach(function (w) { if (w.t >= lastWheel - 250) sum += w.d; });\n"
            + "      return sum >= 300;\n"
            + "    };\n"
            + "    var schedule = function () { if (!timer) timer = setTimeout(tick, 16); };\n"
            + "    var tick = function () {\n"
            + "      timer = 0;\n"
            + "      if (!finger) return;\n"
            + "      if (finger.x !== target.x || finger.y !== target.y) {\n"
            + "        finger = { x: toward(finger.x, target.x), y: toward(finger.y, target.y) };\n"
            + "        send('contact', finger);\n"
            + "      } else if (carry.x || carry.y) {\n"
            + "        var r = canvas.getBoundingClientRect(), at = finger, rest = carry;\n"
            + "        up(false);\n"
            + "        down({ x: rest.x > 0 ? r.left : rest.x < 0 ? r.right : at.x, y: rest.y > 0 ? r.top : rest.y < 0 ? r.bottom : at.y }, rest);\n"
            + "        return;\n"
            + "      } else if (Date.now() - lastWheel >= 120) {\n"
            + "        up(fast());\n"
            + "        return;\n"
            + "      }\n"
            + "      schedule();\n"
            + "    };\n"
            + "    var cancel = function () {\n"
            + "      if (timer) { clearTimeout(timer); timer = 0; }\n"
            + "      origin = null;\n"
            + "      pending = { x: 0, y: 0 };\n"
            + "      up(false);\n"
            + "    };\n"
            + "    window.addEventListener('wheel', function (e) {\n"
            + "      if (e.ctrlKey) return;\n"
            + "      e.preventDefault();\n"
            + "      if (!canvas.width || !canvas.height || (typeof activePointer !== 'undefined' && activePointer !== null)) return;\n"
            + "      var unit = e.deltaMode === 1 ? 40 : e.deltaMode === 2 ? 800 : 1;\n"
            + "      var dx = e.deltaX * unit, dy = e.deltaY * unit;\n"
            + "      if (e.shiftKey && !dx) { dx = dy; dy = 0; }\n"
            + "      if (!dx && !dy) return;\n"
            + "      var now = Date.now(), r = canvas.getBoundingClientRect(), scale = r.height * 0.001;\n"
            + "      if (!finger && now - lastWheel > 120) { origin = null; pending = { x: 0, y: 0 }; }\n"
            + "      lastWheel = now;\n"
            + "      recent = recent.filter(function (w) { return w.t >= now - 250; });\n"
            + "      recent.push({ t: now, d: Math.abs(dx) + Math.abs(dy) });\n"
            + "      var move = { x: -dx * scale, y: -dy * scale };\n"
            + "      if (finger) { aim(move); schedule(); return; }\n"
            + "      if (!origin) origin = { x: e.clientX, y: e.clientY };\n"
            + "      pending = { x: pending.x + move.x, y: pending.y + move.y };\n"
            + "      if (Math.abs(pending.x) + Math.abs(pending.y) < 12) return;\n"
            + "      var start = origin, go = pending;\n"
            + "      origin = null;\n"
            + "      pending = { x: 0, y: 0 };\n"
            + "      down(start, go);\n"
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

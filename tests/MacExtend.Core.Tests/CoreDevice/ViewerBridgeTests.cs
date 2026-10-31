using System;
using MacExtend.Core.CoreDevice;

namespace MacExtend.Core.Tests.CoreDevice;

public class ViewerBridgeTests
{
    private const string Stage = "#1B2431";

    [Theory]
    [InlineData("http://127.0.0.1:52000/")]
    [InlineData("http://127.0.0.1:52000")]
    [InlineData("HTTP://127.0.0.1:52000/")]
    [InlineData("http://127.0.0.1:52000/#top")]
    public void TheSessionsOwnViewerPageMayLoad(string uri)
    {
        Assert.True(ViewerBridge.IsAllowedNavigation(uri, 52000));
    }

    [Fact]
    public void TheBlankPageMayLoadOnlyBetweenSessions()
    {
        // the window clears its viewer port before it shows the blank page
        Assert.True(ViewerBridge.IsAllowedNavigation("about:blank", 0));
    }

    [Theory]
    [InlineData(52000)]
    [InlineData(1024)]
    public void TheBlankPageIsRefusedWhileAViewerIsLoaded(int port)
    {
        // after a cable restart the history is viewer, blank, viewer: Back would leave a black window with no message
        Assert.False(ViewerBridge.IsAllowedNavigation("about:blank", port));
    }

    [Theory]
    [InlineData("http://127.0.0.1:52001/")]
    [InlineData("https://127.0.0.1:52000/")]
    [InlineData("http://localhost:52000/")]
    [InlineData("http://[::1]:52000/")]
    [InlineData("http://127.0.0.2:52000/")]
    [InlineData("http://127.0.0.1:52000/viewer.js")]
    [InlineData("http://127.0.0.1:52000/?compensate=0")]
    [InlineData("http://user@127.0.0.1:52000/")]
    [InlineData("http://127.0.0.1:52000@example.com/")]
    [InlineData("http://127.0.0.1.example.com:52000/")]
    [InlineData("https://example.com/")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("about:blank?x")]
    [InlineData("about:srcdoc")]
    [InlineData("/relative")]
    [InlineData("")]
    [InlineData(null)]
    public void EverythingElseIsBlocked(string uri)
    {
        Assert.False(ViewerBridge.IsAllowedNavigation(uri, 52000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(70000)]
    public void NoViewerPageLoadsWithoutAServingPort(int port)
    {
        Assert.False(ViewerBridge.IsAllowedNavigation("http://127.0.0.1:" + port + "/", port));
    }

    [Fact]
    public void HidesEverythingButTheCanvasAndItsOfflineOverlay()
    {
        var script = ViewerBridge.DocumentScript(Stage);

        Assert.Contains("#topbar,#left-tray,#right-tray,#bottom-row,#hw-tooltip,#help-overlay,.hw{display:none!important}", script);
        Assert.Contains("#stage::before{display:none!important}", script);
        Assert.Contains("document.body.classList.remove('frame-on')", script);
        Assert.DoesNotContain("#offline-overlay{display", script);
        Assert.DoesNotContain("#c{display", script);
    }

    [Fact]
    public void FitsThePictureToTheWindowInsteadOfTheViewersOwnMargins()
    {
        var script = ViewerBridge.DocumentScript(Stage);

        // viewer.js sets the canvas size inline and keeps 88 x 150 px for the chrome this page hides
        Assert.Contains("#c{width:auto!important;height:auto!important;max-width:calc(100vw - 16px);max-height:calc(100vh - 16px)", script);
    }

    [Fact]
    public void TurnsOffTheBezelZoomAndClipboardSyncBeforeTheViewerReadsThem()
    {
        var script = ViewerBridge.DocumentScript(Stage);

        Assert.Contains("localStorage.setItem('frameOn', 'false')", script);
        Assert.Contains("localStorage.removeItem('zoom')", script);
        Assert.Contains("localStorage.setItem('clipboardSync', 'false')", script);
        // the page is the viewer only while it is served over http; about:blank between sessions is left alone
        Assert.StartsWith("(function () {\n  if (location.protocol !== 'http:') return;", script);
    }

    [Fact]
    public void KeysTheViewerWouldKeepForItselfGoToTheDevice()
    {
        var script = ViewerBridge.DocumentScript(Stage);

        // '?' opens the viewer's help, which this page hides: the device would get no key until Esc.
        // Ctrl+P saves a screenshot, Ctrl+= / Ctrl+- / Ctrl+0 zoom. Their key is masked before viewer.js sees it.
        Assert.Contains("window.addEventListener('keydown', function (e) {", script);
        Assert.Contains("(e.key === '?' && !e.ctrlKey) || (e.ctrlKey && 'p=+-0'.indexOf(e.key.toLowerCase()) >= 0)", script);
        Assert.Contains("Object.defineProperty(e, 'key', { value: 'Unidentified' })", script);
        Assert.Contains("}, true);", script);
    }

    [Fact]
    public void GivesTheViewerTheWholeDecodedPictureWhenTheDecoderCutsItShort()
    {
        var script = ViewerBridge.DocumentScript(Stage);

        // Edge's HEVC decoder (WebView2 too) decodes 832x1808 but marks only 832x720 visible and shows it as 1280x720,
        // WebCodecs' default for a config without a size: the viewer drew the top 40 % stretched sideways
        Assert.Contains("var NativeDecoder = window.VideoDecoder;", script);
        Assert.Contains("return new NativeDecoder({ output: function (frame) { output(wholePicture(frame)); }, error: init.error });", script);
        Assert.Contains("Decoder.isConfigSupported = function (config) { return NativeDecoder.isConfigSupported(config); };", script);
        Assert.Contains("window.VideoDecoder = Decoder;", script);
        // a real conformance window crops less than one 64 px coding tree block, and such a frame is left as it is
        Assert.Contains("if (!r || (w - r.width < 64 && h - r.height < 64)) return frame;", script);
        Assert.Contains("new VideoFrame(frame, { visibleRect: { x: 0, y: 0, width: w, height: h }, displayWidth: w, displayHeight: h })", script);
        Assert.Contains("frame.close();", script);
        // before viewer.js builds its decoder, and only on the viewer page
        Assert.True(script.IndexOf("window.VideoDecoder = Decoder;", StringComparison.Ordinal)
                    < script.IndexOf("document.addEventListener('DOMContentLoaded'", StringComparison.Ordinal));
    }

    [Fact]
    public void TheWheelScrollsTheDeviceWithAFingerDrag()
    {
        var script = ViewerBridge.DocumentScript(Stage);

        // viewer.js has no wheel at all: the window's own listener drags a finger through /touch and the viewer's own
        // touchCoords, so a rotated picture scrolls the way it looks
        Assert.Contains("window.addEventListener('wheel', function (e) {", script);
        Assert.Contains("}, { passive: false });", script);
        Assert.Contains("var c = touchCoords({ clientX: p.x, clientY: p.y });", script);
        Assert.Contains("return fetch('/touch', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ type: type, x: c.x, y: c.y }) });", script);
        // installed once the viewer has run, and only if it still has touchCoords
        Assert.Contains("if (c && typeof touchCoords === 'function') wheelDrag(c);", script);
    }

    [Fact]
    public void TheWheelNeverTapsOrStartsASystemGesture()
    {
        var script = ViewerBridge.DocumentScript(Stage);

        // the left button's own finger wins; Ctrl+wheel (a touchpad pinch) is left alone
        Assert.Contains("(typeof activePointer !== 'undefined' && activePointer !== null)) return;", script);
        Assert.Contains("if (e.ctrlKey) return;", script);
        // no finger goes down until the wheel asks for more than a tap
        Assert.Contains("if (Math.abs(pending.x) + Math.abs(pending.y) < 12) return;", script);
        // the finger stays away from the edges where iOS starts Home, Control Center and Back
        Assert.Contains("x: Math.min(r.right - r.width * 0.1, Math.max(r.left + r.width * 0.1, p.x))", script);
        Assert.Contains("y: Math.min(r.bottom - r.height * 0.2, Math.max(r.top + r.height * 0.2, p.y))", script);
        // any press, blur or hidden page lifts it
        Assert.Contains("window.addEventListener('pointerdown', cancel, true);", script);
        Assert.Contains("window.addEventListener('blur', cancel);", script);
        Assert.Contains("document.addEventListener('visibilitychange', cancel);", script);
    }

    [Fact]
    public void TheWheelMovesOneFingerAndFlicksWhenItWasTurningFast()
    {
        var script = ViewerBridge.DocumentScript(Stage);

        // a notch (100 px) moves the finger a tenth of the picture; down moves the content up, Shift turns it sideways
        Assert.Contains("scale = r.height * 0.001;", script);
        Assert.Contains("var move = { x: -dx * scale, y: -dy * scale };", script);
        Assert.Contains("if (e.shiftKey && !dx) { dx = dy; dy = 0; }", script);
        // notches in a row move the finger that is already down
        Assert.Contains("if (finger) { aim(move); schedule(); return; }", script);
        // three notches within the last 250 ms flick on release
        Assert.Contains("return sum >= 300;", script);
        Assert.Contains("up(fast());", script);
        // one request after another: a release never overtakes the moves before it
        Assert.Contains("queue = queue.then(function () {", script);
    }

    [Fact]
    public void SaysInVietnameseWhatToDoWhenThePictureStops()
    {
        var script = ViewerBridge.DocumentScript(Stage);

        Assert.Equal("Đang chờ hình. Nếu màn hình thiết bị đang tắt, hãy mở khóa thiết bị.", ViewerBridge.OfflineText);
        Assert.Contains("document.querySelector('#offline-overlay span')", script);
        Assert.Contains("span.textContent = " + ViewerBridge.JsString(ViewerBridge.OfflineText), script);
        // the viewer keeps that label on one line, which a portrait picture cannot hold in Vietnamese
        Assert.Contains("#offline-overlay span{white-space:normal!important", script);
    }

    [Fact]
    public void PaintsThePageInTheStageColour()
    {
        Assert.Contains("background:#1B2431!important", ViewerBridge.DocumentScript("#1B2431"));
        Assert.Contains("background:#10151d!important", ViewerBridge.DocumentScript("#10151d"));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#12345G")]
    [InlineData("#123456;}body{display:none")]
    [InlineData("';alert(1)//")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsAColourThatIsNotSixHexDigits(string colour)
    {
        Assert.Throws<ArgumentException>(() => ViewerBridge.DocumentScript(colour));
    }

    [Fact]
    public void RotateClicksTheViewersOwnHiddenButtons()
    {
        // viewer.js turns its picture (setVisualRotation) before POST /rotate, so the window clicks its buttons
        Assert.Contains("document.getElementById('rotate-left')", ViewerBridge.RotateLeft);
        Assert.Contains("document.getElementById('rotate-right')", ViewerBridge.RotateRight);
        Assert.Contains("b.click()", ViewerBridge.RotateLeft);
        Assert.Contains("b.click()", ViewerBridge.RotateRight);
    }

    [Fact]
    public void FocusGoesToTheCanvasSoKeysReachTheDevice()
    {
        Assert.Contains("document.getElementById('c')", ViewerBridge.FocusCanvas);
        Assert.Contains("c.focus()", ViewerBridge.FocusCanvas);
    }

    [Theory]
    [InlineData("abc", "'abc'")]
    [InlineData("it's", "'it\\'s'")]
    [InlineData("a\\b", "'a\\\\b'")]
    [InlineData("a\nb\r", "'a\\nb\\r'")]
    [InlineData("Đang", "'\\u0110ang'")]
    [InlineData("</script>", "'\\u003c/script\\u003e'")]
    [InlineData("\u2028", "'\\u2028'")]
    public void JsStringEscapesEverythingButPlainAscii(string text, string expected)
    {
        Assert.Equal(expected, ViewerBridge.JsString(text));
    }
}

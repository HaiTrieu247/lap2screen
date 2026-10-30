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

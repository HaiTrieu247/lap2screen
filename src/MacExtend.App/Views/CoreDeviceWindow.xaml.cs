using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.Services.CoreDevice;
using MacExtend.Themes;
using MacExtend.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MacExtend.Views
{
    /// <summary>
    /// "Điều khiển trực tiếp": serve-web's viewer in a WebView2, showing only its canvas (<see cref="ViewerBridge"/>),
    /// under a Vietnamese toolbar. It reads nothing of the session but the service's <see cref="SessionView"/> snapshot,
    /// on the UI thread. WPF cannot draw over the WebView2, so status sits in a band above the picture, or in the
    /// placeholder that stands in while there is no picture. Closing the window stops the session in the background.
    /// </summary>
    internal partial class CoreDeviceWindow : Window
    {
        /// <summary>Microsoft's Evergreen bootstrapper for the WebView2 Runtime.</summary>
        private const string WebView2Download = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        private const string MissingTitle = "Máy chưa có Microsoft Edge WebView2 Runtime";
        private const string MissingDetail = "Cửa sổ này cần WebView2 Runtime để hiện màn hình thiết bị. Tải về và cài đặt, rồi mở lại Điều khiển trực tiếp.";
        private const string BrokenTitle = "Không mở được khung hình";
        private const string BrokenDetail = "Microsoft Edge WebView2 Runtime không khởi động được. Cài lại WebView2 Runtime, rồi mở lại Điều khiển trực tiếp.";

        private static readonly TimeSpan NoticeTime = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ReloadDelay = TimeSpan.FromSeconds(2);
        // a WebView2 whose browser process died is made again, this many times per window
        private const int MaxViewerStarts = 3;

        private readonly CoreDeviceService _service;
        private readonly SettingsService _settings;
        private readonly Action _openSetup;
        private readonly Action _retry;
        private readonly DispatcherTimer _noticeTimer;
        private readonly DispatcherTimer _reloadTimer;

        // created in code: never while the runtime is missing, again after its browser process died
        private WebView2 _viewer;
        private bool _viewerReady;
        private int _viewerStarts;
        // N7: once the WebView2 cannot run here this stays in the placeholder until the window closes
        private string _unavailableTitle;
        private string _unavailableDetail;
        // the viewer page the WebView2 shows; 0 and null while it shows the blank page
        private int _loadedPort;
        private string _loadedUrl;
        private bool _pageFailed;
        private string _notice;
        private bool _clipboardBusy;
        private bool _closed;
        private bool _fullscreen;
        private WindowState _stateBeforeFullscreen;
        private bool _setupOpen;

        public CoreDeviceWindow(CoreDeviceService service, SettingsService settings, Action openSetup, Action retry)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _openSetup = openSetup ?? throw new ArgumentNullException(nameof(openSetup));
            _retry = retry ?? throw new ArgumentNullException(nameof(retry));
            InitializeComponent();
            RestoreSavedBounds();
            UpdateHint();
            ApplyPin();
            TextPopup.CustomPopupPlacementCallback = PlaceTextPopup;
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever };
            Spinner.IsVisibleChanged += (_, __) => SpinnerTurn.BeginAnimation(RotateTransform.AngleProperty, Spinner.IsVisible ? spin : null);

            _noticeTimer = new DispatcherTimer { Interval = NoticeTime };
            _noticeTimer.Tick += (_, __) =>
            {
                _noticeTimer.Stop();
                _notice = null;
                UpdateBand();
            };
            _reloadTimer = new DispatcherTimer { Interval = ReloadDelay };
            _reloadTimer.Tick += (_, __) =>
            {
                _reloadTimer.Stop();
                if (_pageFailed && _loadedUrl != null) Navigate(_loadedUrl);
            };

            _service.StateChanged += Refresh;
            SourceInitialized += (_, __) => ThemeManager.ApplyWindowFrame(this);
            Loaded += OnLoaded;
            Activated += (_, __) => FocusViewer();
            PreviewKeyDown += OnKey;
            Closing += (_, __) => SaveBounds();
            Closed += OnClosed;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            Refresh();
            await StartViewerAsync();
            Refresh();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _closed = true;
            _service.StateChanged -= Refresh;
            _noticeTimer.Stop();
            _reloadTimer.Stop();
            DropViewer();
            TextPopup.IsOpen = false;
            // the stop goes on in the background (serve-web's cleanup, a Wi-Fi tunnel); the window is gone at once
            _ = _service.StopAsync();
        }

        // ---- the session -------------------------------------------------------------------

        /// <summary>Shows the service's current snapshot. UI thread: StateChanged is always raised there.</summary>
        private void Refresh()
        {
            if (_closed) return;
            var view = _service.View;
            RouteText.Text = CoreDeviceMessages.Route(view.Route);
            RouteBadge.Visibility = view.Route == CoreDeviceRoute.None || _unavailableTitle != null ? Visibility.Collapsed : Visibility.Visible;

            if (_unavailableTitle != null)
            {
                // N7: sticky. No phase replaces it, and the session's end does not close the window under it. Nothing
                // can show the device here, so a session, also one opened again later from the tray, is stopped
                // rather than keep the device's camera and microphone blocked.
                if (view.IsActive && (view.Phase != SessionPhase.Stopping || view.WaitingForPreviousStop)) _ = _service.StopAsync();
                ShowPlaceholder(false, _unavailableTitle, _unavailableDetail, download: true);
                UpdateTools(false);
                UpdateBand();
                return;
            }
            if (view.Phase == SessionPhase.Idle || view.Phase == SessionPhase.Stopped)
            {
                Close();
                return;
            }

            var picture = view.Port != 0 && _viewerReady;
            if (picture)
            {
                // a new session, or serve-web serving again on a new port after the cable came back (E3)
                if (view.Port != _loadedPort) Load(view.Port, view.ViewerUrl);
                _viewer.Visibility = Visibility.Visible;
                Placeholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                Unload();
                if (_viewer != null) _viewer.Visibility = Visibility.Collapsed;
                if (view.Phase == SessionPhase.Failed)
                {
                    ShowPlaceholder(false, CoreDeviceMessages.Phase(SessionPhase.Failed),
                                    view.Failure == SessionFailure.None ? null : view.StatusText,
                                    retry: true, setup: CoreDeviceMessages.NeedsSetup(view.Failure));
                }
                else
                {
                    // served already while the WebView2 still starts: for the user that is still connecting.
                    // A lost cable waits for the user, so it gets no spinner.
                    ShowPlaceholder(view.Phase != SessionPhase.WaitingForDevice,
                                    view.Phase == SessionPhase.Ready ? CoreDeviceMessages.Phase(SessionPhase.Starting) : view.StatusText, null);
                }
            }
            UpdateTools(picture);
            UpdateBand();
        }

        private void ShowPlaceholder(bool busy, string title, string detail, bool retry = false, bool setup = false, bool download = false)
        {
            Placeholder.Visibility = Visibility.Visible;
            Spinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ProblemGlyph.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            PlaceholderTitle.Text = title ?? "";
            PlaceholderDetail.Text = detail ?? "";
            PlaceholderDetail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
            RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
            SetupButton.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
            DownloadButton.Visibility = download ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Home, rotate and text need the viewer; fullscreen always works.</summary>
        private void UpdateTools(bool picture)
        {
            HomeButton.IsEnabled = picture;
            RotateLeftButton.IsEnabled = picture;
            RotateRightButton.IsEnabled = picture;
            TextButton.IsEnabled = picture;
            if (!picture) TextPopup.IsOpen = false;
        }

        /// <summary>The band above the picture: a short notice first, else the Wi-Fi reconnect, else a page that did not load.</summary>
        private void UpdateBand()
        {
            var view = _service.View;
            var picture = _unavailableTitle == null && _viewerReady && _loadedPort != 0;
            var text = _notice
                ?? (picture && view.Phase == SessionPhase.Reconnecting ? view.StatusText : null)
                ?? (picture && _pageFailed ? "Chưa tải được hình. Đang thử lại..." : null);
            BannerText.Text = text ?? "";
            Banner.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ShowNotice(string text)
        {
            _notice = text;
            _noticeTimer.Stop();
            _noticeTimer.Start();
            UpdateBand();
        }

        // ---- the WebView2 ------------------------------------------------------------------

        private async Task StartViewerAsync()
        {
            if (_closed || _viewer != null || _unavailableTitle != null) return;
            WebView2 viewer = null;
            try
            {
                if (!WebView2Installed())
                {
                    Log("viewer: WebView2 runtime missing");
                    MakeUnavailable(true);
                    return;
                }
                _viewerStarts++;
                var stage = StageColour();
                viewer = new WebView2
                {
                    Visibility = Visibility.Collapsed,
                    DefaultBackgroundColor = System.Drawing.Color.FromArgb(stage.R, stage.G, stage.B),
                };
                viewer.PreviewKeyDown += OnKey;
                Picture.Children.Insert(0, viewer);
                _viewer = viewer;

                var environment = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDataDir);
                if (_viewer != viewer) return;
                await viewer.EnsureCoreWebView2Async(environment);
                if (_viewer != viewer) return;
                var core = viewer.CoreWebView2;
                Lock(core.Settings);
                // the session's own viewer page and the blank page between sessions, nothing else, and never from history
                // (the mouse's back button): after a cable restart that history is viewer, blank, viewer
                core.NavigationStarting += (_, a) => { if (!MayNavigate(a)) a.Cancel = true; };
                core.FrameNavigationStarting += (_, a) => { if (!MayNavigate(a)) a.Cancel = true; };
                core.NewWindowRequested += (_, a) => a.Handled = true;
                core.DownloadStarting += (_, a) =>
                {
                    a.Cancel = true;
                    a.Handled = true;
                };
                core.PermissionRequested += (_, a) => a.State = CoreWebView2PermissionState.Deny;
                core.NavigationCompleted += OnNavigationCompleted;
                // bound to its control: after its browser process crashed, the control's CoreWebView2 getter throws
                core.ProcessFailed += (_, a) => OnProcessFailed(viewer, a);
                await core.AddScriptToExecuteOnDocumentCreatedAsync(ViewerBridge.DocumentScript(Hex(stage)));
                if (_viewer != viewer) return;
                _viewerReady = true;
            }
            catch (Exception e)
            {
                // closed or replaced meanwhile: whatever failed no longer matters
                if (_closed || (viewer != null && _viewer != viewer)) return;
                Log("viewer: WebView2 did not start (" + e.GetType().Name + ")");
                DropViewer();
                MakeUnavailable(e is WebView2RuntimeNotFoundException);
            }
        }

        private static bool WebView2Installed()
        {
            try
            {
                return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString(null));
            }
            catch (WebView2RuntimeNotFoundException)
            {
                return false;
            }
        }

        private bool MayNavigate(CoreWebView2NavigationStartingEventArgs a) =>
            !IsHistory(a) && ViewerBridge.IsAllowedNavigation(a.Uri, _loadedPort);

        private static bool IsHistory(CoreWebView2NavigationStartingEventArgs a)
        {
            try
            {
                return a.NavigationKind == CoreWebView2NavigationKind.BackOrForward;
            }
            catch (Exception e) when (e is NotImplementedException || e is InvalidCastException)
            {
                // a runtime without NavigationKind: the address check alone still keeps history from the blank page
                return false;
            }
        }

        /// <summary>No context menu, DevTools, zoom, browser keys, status bar, autofill, swipe or error page: only the viewer.</summary>
        private static void Lock(CoreWebView2Settings s)
        {
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = false;
            s.IsZoomControlEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsStatusBarEnabled = false;
            s.AreHostObjectsAllowed = false;
            s.IsWebMessageEnabled = false;
            s.IsBuiltInErrorPageEnabled = false;
            // newer settings: a runtime too old for one of them still shows the viewer
            TrySet(() => s.IsPinchZoomEnabled = false);
            TrySet(() => s.IsSwipeNavigationEnabled = false);
            TrySet(() => s.IsGeneralAutofillEnabled = false);
            TrySet(() => s.IsPasswordAutosaveEnabled = false);
            // a loopback page has no reputation to check, and its address need not leave the laptop
            TrySet(() => s.IsReputationCheckingRequired = false);
        }

        private static void TrySet(Action set)
        {
            try
            {
                set();
            }
            catch (Exception e) when (e is NotImplementedException || e is InvalidCastException)
            {
                // the runtime lacks that interface ("Unable to cast to ...ICoreWebView2Settings<n>")
            }
        }

        /// <summary>N7: the WebView2 cannot run here. Refresh keeps saying so and stops the session.</summary>
        private void MakeUnavailable(bool missing)
        {
            _unavailableTitle = missing ? MissingTitle : BrokenTitle;
            _unavailableDetail = missing ? MissingDetail : BrokenDetail;
            Refresh();
        }

        private void DropViewer()
        {
            var viewer = _viewer;
            _viewer = null;
            _viewerReady = false;
            _loadedPort = 0;
            _loadedUrl = null;
            _pageFailed = false;
            _reloadTimer.Stop();
            if (viewer == null) return;
            viewer.PreviewKeyDown -= OnKey;
            Picture.Children.Remove(viewer);
            try
            {
                viewer.Dispose();
            }
            catch (Exception e) when (e is InvalidOperationException || e is COMException || e is ObjectDisposedException)
            {
                // a control whose browser process crashed may fail to release what is already gone
            }
        }

        private void Load(int port, string url)
        {
            // set before navigating: NavigationStarting lets only this port through
            _loadedPort = port;
            _loadedUrl = url;
            _pageFailed = false;
            _reloadTimer.Stop();
            Navigate(url);
        }

        /// <summary>The blank page: the old viewer's streams and requests end with it.</summary>
        private void Unload()
        {
            if (_loadedPort == 0) return;
            _loadedPort = 0;
            _loadedUrl = null;
            _pageFailed = false;
            _reloadTimer.Stop();
            Navigate("about:blank");
        }

        private void Navigate(string url)
        {
            if (!_viewerReady) return;
            try
            {
                _viewer.CoreWebView2.Navigate(url);
            }
            catch (Exception e) when (e is COMException || e is InvalidOperationException)
            {
                // the browser process is going away; ProcessFailed makes a new WebView2
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_closed || _loadedPort == 0) return;
            if (e.IsSuccess)
            {
                _pageFailed = false;
                FocusViewer();
            }
            else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
            {
                // serve-web answered its viewer moments ago, so this is short-lived: try again until the phase changes
                _pageFailed = true;
                _reloadTimer.Stop();
                _reloadTimer.Start();
            }
            UpdateBand();
        }

        /// <summary>Reads only the event's args: once the browser process is gone, the control's CoreWebView2 getter throws.</summary>
        private void OnProcessFailed(WebView2 viewer, CoreWebView2ProcessFailedEventArgs e)
        {
            if (_closed || _viewer != viewer) return;
            var kind = e.ProcessFailedKind;
            Log("viewer: WebView2 " + kind);
            switch (kind)
            {
                case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                    // this WebView2 is done: nothing may use it any more (Navigate, RunScript and FocusViewer check
                    // _viewerReady), and a new one is made outside its own event, once for this one
                    _viewerReady = false;
                    _ = Dispatcher.BeginInvoke(new Action(() => RestartViewer(viewer)));
                    break;
                case CoreWebView2ProcessFailedKind.RenderProcessExited:
                case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                    if (_loadedUrl != null) Navigate(_loadedUrl);
                    break;
            }
        }

        private async void RestartViewer(WebView2 failed)
        {
            if (_closed || _viewer != failed) return;
            // DropViewer takes the dead control out and disposes it without touching its CoreWebView2
            DropViewer();
            if (_viewerStarts >= MaxViewerStarts)
            {
                MakeUnavailable(false);
                return;
            }
            Refresh();
            await StartViewerAsync();
            Refresh();
        }

        private void FocusViewer()
        {
            // keys go to the device only while the canvas has the focus
            if (!_viewerReady || _loadedPort == 0 || !IsActive || TextPopup.IsOpen) return;
            _viewer.Focus();
            RunScript(ViewerBridge.FocusCanvas);
        }

        private async void RunScript(string script)
        {
            var viewer = _viewer;
            if (!_viewerReady || viewer == null) return;
            try
            {
                await viewer.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception)
            {
                // the page is changing or gone; the next click tries again
            }
        }

        private Color StageColour() =>
            TryFindResource("Stage.Background.Color") is Color c ? c : Color.FromRgb(0x1B, 0x24, 0x31);

        private static string Hex(Color c) =>
            "#" + c.R.ToString("X2", CultureInfo.InvariantCulture) + c.G.ToString("X2", CultureInfo.InvariantCulture) + c.B.ToString("X2", CultureInfo.InvariantCulture);

        // ---- toolbar -----------------------------------------------------------------------

        private async void OnHome(object sender, RoutedEventArgs e)
        {
            var port = _service.Port;
            if (port == 0) return;
            try
            {
                // an answer only means serve-web queued the press for the device, so success shows nothing (C12)
                using (var control = new ServeWebControl(port)) await control.HomeAsync(CancellationToken.None);
            }
            catch (CoreDeviceClientException)
            {
                ShowNotice("Không gửi được nút Home tới thiết bị. Thử lại.");
            }
        }

        private void OnRotateLeft(object sender, RoutedEventArgs e) => RunScript(ViewerBridge.RotateLeft);

        private void OnRotateRight(object sender, RoutedEventArgs e) => RunScript(ViewerBridge.RotateRight);

        // N3
        private void OnFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

        /// <summary>
        /// F11, from WPF or from the WebView2, which hands its accelerator keys to WPF as PreviewKeyDown first. Esc is
        /// not used: it belongs to the device.
        /// </summary>
        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.F11 || e.Handled) return;
            e.Handled = true;
            if (!e.IsRepeat) ToggleFullscreen();
        }

        private void ToggleFullscreen()
        {
            _fullscreen = !_fullscreen;
            if (_fullscreen)
            {
                _stateBeforeFullscreen = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
                WindowStyle = WindowStyle.None;
                // no resize border around the screen; Normal first so a maximized window covers the taskbar too
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                WindowState = _stateBeforeFullscreen;
            }
            FullscreenGlyph.Text = _fullscreen ? "\uE73F" : "\uE740"; // BackToWindow, FullScreen
            FullscreenButton.ToolTip = _fullscreen ? "Thoát toàn màn hình (F11)" : "Toàn màn hình (F11)";
            UpdateHint();
            ApplyPin();
            FocusViewer();
        }

        // ---- the pin -------------------------------------------------------------------------

        /// <summary>
        /// The setup window is open. The pin waits meanwhile: this window's own "Mở Thiết lập" opens the setup, which
        /// would otherwise open behind it.
        /// </summary>
        public bool SetupOpen
        {
            set
            {
                if (_closed) return;
                _setupOpen = value;
                ApplyPin();
            }
        }

        private void OnPin(object sender, RoutedEventArgs e)
        {
            _settings.Current.DirectOnTop = !_settings.Current.DirectOnTop;
            _settings.ScheduleSave();
            ApplyPin();
        }

        /// <summary>
        /// The user's pin, but never full screen: a pinned full-screen window would stay over the whole monitor after
        /// Alt+Tab. The button shows the user's choice either way.
        /// </summary>
        private void ApplyPin()
        {
            var pinned = _settings.Current.DirectOnTop;
            Topmost = pinned && !_fullscreen && !_setupOpen;
            PinButton.Style = (Style)FindResource(pinned ? "ToolButtonOn" : "ToolButton");
            PinGlyph.Text = pinned ? "\uE841" : "\uE718"; // PinnedFill, Pin
            PinButton.ToolTip = pinned ? "Bỏ ghim khỏi trên cùng" : "Ghim cửa sổ lên trên cùng";
        }

        // ---- text ----------------------------------------------------------------------------

        private void OnText(object sender, RoutedEventArgs e)
        {
            if (!_clipboardBusy) SetClipStatus(null);
            TextPopup.IsOpen = true;
        }

        /// <summary>Right-aligned under the toolbar, below the text button.</summary>
        private static CustomPopupPlacement[] PlaceTextPopup(Size popupSize, Size targetSize, Point offset) =>
            new[] { new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height), PopupPrimaryAxis.Horizontal) };

        private void OnTextPopupOpened(object sender, EventArgs e)
        {
            // a click on the button while the sheet is open only closes it (StaysOpen=False), it does not open it again
            TextButton.IsHitTestVisible = false;
            ClipText.Focus();
            Keyboard.Focus(ClipText);
        }

        private void OnTextPopupClosed(object sender, EventArgs e)
        {
            TextButton.IsHitTestVisible = true;
            FocusViewer();
        }

        private async void OnSendText(object sender, RoutedEventArgs e)
        {
            var text = ClipText.Text;
            if (string.IsNullOrEmpty(text))
            {
                SetClipStatus("Nhập chữ cần gửi trước.");
                return;
            }
            if (await ClipboardCallAsync("Đang gửi...", c => c.SendTextAsync(text, CancellationToken.None)))
            {
                SetClipStatus("Đã gửi vào clipboard của thiết bị. Chạm giữ ô nhập trên thiết bị rồi chọn Dán.");
            }
        }

        private async void OnGetText(object sender, RoutedEventArgs e)
        {
            string text = null;
            if (!await ClipboardCallAsync("Đang lấy...", async c => text = await c.GetTextAsync(CancellationToken.None))) return;
            if (string.IsNullOrEmpty(text))
            {
                SetClipStatus("Clipboard của thiết bị không có chữ.");
                return;
            }
            ClipText.Text = text;
            try
            {
                Clipboard.SetText(text);
                SetClipStatus("Đã chép vào clipboard của Windows.");
            }
            catch (ExternalException)
            {
                SetClipStatus("Đã lấy chữ, nhưng clipboard của Windows đang bận. Chọn chữ rồi nhấn Ctrl+C.");
            }
        }

        /// <summary>One clipboard call at a time, with the longer clipboard timeout (Wi-Fi). Never logs the text.</summary>
        private async Task<bool> ClipboardCallAsync(string busyText, Func<ServeWebControl, Task> call)
        {
            var port = _service.Port;
            if (port == 0 || _clipboardBusy) return false;
            _clipboardBusy = true;
            GetTextButton.IsEnabled = false;
            SendTextButton.IsEnabled = false;
            SetClipStatus(busyText);
            try
            {
                using (var control = new ServeWebControl(port, ServeWebControl.ClipboardTimeout)) await call(control);
                return true;
            }
            catch (CoreDeviceClientException)
            {
                SetClipStatus("Thiết bị chưa phản hồi. Thử lại.");
                return false;
            }
            finally
            {
                _clipboardBusy = false;
                GetTextButton.IsEnabled = true;
                SendTextButton.IsEnabled = true;
            }
        }

        private void SetClipStatus(string text)
        {
            ClipStatus.Text = text ?? "";
            ClipStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---- placeholder actions and the hint ----------------------------------------------

        private void OnRetry(object sender, RoutedEventArgs e) => _retry();

        private void OnSetup(object sender, RoutedEventArgs e) => _openSetup();

        private void OnDownloadRuntime(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(WebView2Download) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
            {
                // no browser to open it: the address goes to the clipboard instead
                try
                {
                    Clipboard.SetText(WebView2Download);
                    ShowNotice("Không mở được trình duyệt. Đã chép đường dẫn tải WebView2 Runtime vào clipboard.");
                }
                catch (ExternalException)
                {
                    ShowNotice("Không mở được trình duyệt. Tải WebView2 Runtime tại " + WebView2Download);
                }
            }
        }

        private void OnDismissHint(object sender, RoutedEventArgs e)
        {
            _settings.Current.DirectHintDismissed = true;
            _settings.ScheduleSave();
            UpdateHint();
        }

        private void UpdateHint() =>
            Hint.Visibility = _settings.Current.DirectHintDismissed || _fullscreen ? Visibility.Collapsed : Visibility.Visible;

        // ---- remembered bounds ---------------------------------------------------------------

        /// <summary>N2. C13: the saved text is opaque; anything unusable, or off every screen now, means the default position.</summary>
        private void RestoreSavedBounds()
        {
            if (WindowBounds.Place(_settings.Current.DirectWindowBounds, ScreenWorkAreas(), MinWidth, MinHeight) is WindowBounds b)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = b.Left;
                Top = b.Top;
                Width = b.Width;
                Height = b.Height;
                return;
            }
            // centred, and short enough for a laptop at 150 % (1080p leaves about 690 units under the taskbar)
            Height = Math.Max(MinHeight, Math.Min(Height, SystemParameters.WorkArea.Height - 24));
        }

        private void SaveBounds()
        {
            var normal = WindowState == WindowState.Normal && !_fullscreen ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if (normal.IsEmpty || !(normal.Width > 0) || !(normal.Height > 0)) return;
            _settings.Current.DirectWindowBounds = new WindowBounds(normal.X, normal.Y, normal.Width, normal.Height).Format();
            _settings.ScheduleSave();
        }

        /// <summary>Every screen's work area in WPF units: Screen reports pixels, WPF places a new window at the system DPI.</summary>
        private static IReadOnlyList<WindowBounds> ScreenWorkAreas()
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen;
            var scale = primary != null && SystemParameters.PrimaryScreenWidth > 0 ? primary.Bounds.Width / SystemParameters.PrimaryScreenWidth : 1;
            if (!(scale > 0)) scale = 1;
            return System.Windows.Forms.Screen.AllScreens
                .Select(s => s.WorkingArea)
                .Select(a => new WindowBounds(a.Left / scale, a.Top / scale, a.Width / scale, a.Height / scale))
                .ToList();
        }

        /// <summary>coredevice.log, one line per write like the service's: classified states only, never keys or clipboard text.</summary>
        private static void Log(string line)
        {
            try
            {
                using (var log = new RotatingLog(AppPaths.CoreDeviceLog, 512 * 1024)) log.WriteLine("[coredevice] " + line);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // a log that cannot be written never breaks the window
            }
        }
    }
}

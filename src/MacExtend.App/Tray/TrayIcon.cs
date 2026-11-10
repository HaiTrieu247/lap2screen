using System;
using System.Drawing;
using System.Windows.Forms;
using MacExtend.Core;
using MacExtend.Core.CoreDevice;
using MacExtend.Core.Wda;
using MacExtend.Services;
using MacExtend.Services.Bluetooth;
using MacExtend.Services.CoreDevice;
using MacExtend.Services.Wda;
using MacExtend.Themes;

namespace MacExtend.Tray
{
    /// <summary>The notification-area icon: shows the receiver state and offers the few actions needed without the window.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly ReceiverController _controller;
        private readonly BluetoothController _bluetooth;
        private readonly CoreDeviceService _direct;
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _status;
        private readonly ToolStripMenuItem _toggle;
        private readonly ToolStripMenuItem _restart;
        private readonly ToolStripMenuItem _update;
        private readonly ToolStripMenuItem _showMac;
        private readonly ToolStripMenuItem _onTop;
        private readonly ToolStripMenuItem _control;
        private readonly ToolStripMenuItem _directItem;
        private readonly WdaController _wda;
        private readonly ToolStripMenuItem _wdaChoose;
        private Action _installUpdate;
        private bool _hintShown;
        private bool _forcedStopShown;
        private bool _exiting;
        private Icon _current;

        /// <param name="showMac">Brings the AirPlay display back to the front; offered while a device is connected.</param>
        /// <param name="onTop">The "always on top" setting; offered next to <paramref name="showMac"/>.</param>
        /// <param name="bluetooth">Offers to start or stop controlling the device while that is possible.</param>
        /// <param name="direct">The direct-control session: its item says whether a click starts or stops one.</param>
        /// <param name="toggleDirect">Opens a direct-control session, or stops the one running.</param>
        /// <param name="wda">WDA control: its status, and the device choice it may wait for.</param>
        /// <param name="chooseWda">Shows the device picker for WDA control.</param>
        public TrayIcon(ReceiverController controller, BluetoothController bluetooth, CoreDeviceService direct, Action toggleDirect,
                        WdaController wda, Action chooseWda, Action open, Action exit, Func<bool> macDisplayOpen, Action showMac,
                        Func<bool> onTop, Action<bool> setOnTop)
        {
            _controller = controller;
            _bluetooth = bluetooth;
            _direct = direct;
            _wda = wda;

            _status = new ToolStripMenuItem { Enabled = false };
            var openItem = new ToolStripMenuItem("Mở MacExtend", null, (_, __) => open());
            openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
            _toggle = new ToolStripMenuItem("", null, (_, __) => Toggle());
            _restart = new ToolStripMenuItem("Khởi động lại bộ nhận", null, (_, __) => _controller.RestartAsync());
            var exitItem = new ToolStripMenuItem("Thoát MacExtend", null, (_, __) => exit());
            _update = new ToolStripMenuItem("", null, (_, __) => _installUpdate?.Invoke()) { Visible = false };
            _showMac = new ToolStripMenuItem("Hiện màn hình AirPlay", null, (_, __) => showMac()) { Visible = false };
            _onTop = new ToolStripMenuItem("", null, (_, __) => setOnTop(!onTop())) { Visible = false };
            _control = new ToolStripMenuItem("", null, (_, __) => ToggleControl()) { Visible = false };
            _directItem = new ToolStripMenuItem("", null, (_, __) => toggleDirect());
            _wdaChoose = new ToolStripMenuItem(WdaTexts.ChooseDevice + "...", null, (_, __) => chooseWda()) { Visible = false };

            _menu = new ContextMenuStrip { ShowImageMargin = false, Padding = new Padding(0, 4, 0, 4) };
            _menu.Items.AddRange(new ToolStripItem[]
            {
                _status, new ToolStripSeparator(), _showMac, _onTop, _control, _wdaChoose, _directItem, openItem, _toggle, _restart, _update, new ToolStripSeparator(), exitItem,
            });
            // the menu has no check margin, so the item says what a click does; the setting can also change in the window
            _menu.Opening += (_, __) => _onTop.Text = onTop() ? "Tắt luôn ở trên cùng" : "Bật luôn ở trên cùng";
            foreach (ToolStripItem item in _menu.Items)
            {
                item.Padding = new Padding(8, 5, 24, 5);
            }

            _icon = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
            _icon.MouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Left || _exiting) return;
                // while a device is streaming, a click on the icon returns to its display
                if (macDisplayOpen()) showMac();
                else open();
            };

            _controller.StateChanged += Update;
            _bluetooth.StateChanged += Update;
            _direct.StateChanged += Update;
            _wda.StateChanged += Update;
            ThemeManager.Changed += Update;
            Update();
        }

        /// <summary>
        /// The app is exiting and waits for the direct-control session to stop (N5): the icon says "Đang dừng..." and
        /// its menu offers nothing until the app is gone.
        /// </summary>
        public void ShowExiting()
        {
            _exiting = true;
            Update();
        }

        /// <summary>Tells the user once per session that closing the window does not stop the receiver.</summary>
        public void ShowStillRunningHint()
        {
            if (_hintShown) return;
            _hintShown = true;
            _icon.ShowBalloonTip(4000, "MacExtend vẫn đang chạy",
                "Bộ nhận AirPlay vẫn chạy ở nền. Chuột phải vào biểu tượng ở khay hệ thống để thoát.", ToolTipIcon.None);
        }

        /// <summary>A newer release exists: offer it in the menu and say so once.</summary>
        public void ShowUpdate(string version, Action install)
        {
            _installUpdate = install;
            _update.Text = "Cập nhật lên bản " + version;
            _update.Visible = true;
            _icon.ShowBalloonTip(5000, "Có bản MacExtend " + version,
                "Mở MacExtend và bấm Cập nhật. Không cần gỡ bản cũ.", ToolTipIcon.None);
        }

        public void Dispose()
        {
            _controller.StateChanged -= Update;
            _bluetooth.StateChanged -= Update;
            _direct.StateChanged -= Update;
            _wda.StateChanged -= Update;
            ThemeManager.Changed -= Update;
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            _current?.Dispose();
        }

        private void Toggle()
        {
            if (_controller.Phase == ReceiverPhase.Stopped || _controller.Phase == ReceiverPhase.Failed) _controller.Start();
            else _controller.StopAsync();
        }

        private void ToggleControl()
        {
            if (_bluetooth.IsControlling) _bluetooth.End(StopReason.Request);
            else _bluetooth.StartControl();
        }

        private void Update()
        {
            var phase = _controller.Phase;
            string state, text;
            switch (phase)
            {
                case ReceiverPhase.Ready:
                    state = "ready";
                    text = "Sẵn sàng kết nối";
                    break;
                case ReceiverPhase.Connected:
                    state = "connected";
                    text = "Đang nhận AirPlay từ " + (_controller.ClientName ?? "Thiết bị");
                    break;
                case ReceiverPhase.Failed:
                    state = "error";
                    text = "Không chạy được";
                    break;
                case ReceiverPhase.Starting:
                    state = "ready";
                    text = "Đang khởi động";
                    break;
                default:
                    state = "stopped";
                    text = "Đã dừng";
                    break;
            }

            var taskbar = WindowsTheme.SystemUsesLightTheme ? "light" : "dark";
            var next = LoadIcon("tray-" + state + "-" + taskbar + ".ico");
            _icon.Icon = next;
            _current?.Dispose();
            _current = next;

            if (_bluetooth.IsControlling) text = "Đang điều khiển " + _bluetooth.TargetName;
            text = WdaTexts.TrayStatus(_wda.View, _controller.ClientName) ?? text;
            var direct = _direct.View;
            text = DirectControlTexts.TrayStatus(direct) ?? text;
            if (_exiting) text = CoreDeviceMessages.Phase(SessionPhase.Stopping);
            var tip = "MacExtend: " + text;
            _icon.Text = tip.Length > 63 ? tip.Substring(0, 62) + "…" : tip;
            _status.Text = text;
            _toggle.Text = phase == ReceiverPhase.Stopped || phase == ReceiverPhase.Failed ? "Bắt đầu" : "Dừng";
            _restart.Visible = phase != ReceiverPhase.Stopped;
            _showMac.Visible = phase == ReceiverPhase.Connected;
            _onTop.Visible = phase == ReceiverPhase.Connected;
            _control.Visible = _bluetooth.IsControlling || _bluetooth.CanControl;
            _control.Text = _bluetooth.IsControlling ? "Dừng điều khiển" : "Điều khiển " + _bluetooth.TargetName;
            _wdaChoose.Visible = _wda.View.Phase == WdaPhase.Choose;
            _directItem.Text = DirectControlTexts.TrayItem(direct);
            _directItem.Enabled = DirectControlTexts.TrayItemEnabled(direct);
            if (_exiting)
            {
                // nothing to offer while the app waits for the session's stop
                foreach (ToolStripItem item in _menu.Items) item.Visible = item == _status;
            }
            _menu.Renderer = new ModernMenuRenderer(!WindowsTheme.SystemUsesLightTheme);

            // once per session: a stop that had to kill serve-web may have left the camera and microphone held
            if (direct.IsActive) _forcedStopShown = false;
            else if (DirectControlTexts.WarnForcedStop(direct) && !_forcedStopShown)
            {
                _forcedStopShown = true;
                _icon.ShowBalloonTip(5000, DirectControlTexts.ForcedStopTitle, DirectControlTexts.ForcedStopText, ToolTipIcon.None);
            }
        }

        private static Icon LoadIcon(string name)
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/MacExtend;component/Assets/" + name));
            using (var stream = info.Stream)
            {
                return new Icon(stream, SystemInformation.SmallIconSize);
            }
        }
    }
}

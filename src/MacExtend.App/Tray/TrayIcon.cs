using System;
using System.Drawing;
using System.Windows.Forms;
using MacExtend.Core;
using MacExtend.Services;
using MacExtend.Themes;

namespace MacExtend.Tray
{
    /// <summary>The notification-area icon: shows the receiver state and offers the few actions needed without the window.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly ReceiverController _controller;
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _status;
        private readonly ToolStripMenuItem _toggle;
        private readonly ToolStripMenuItem _restart;
        private readonly ToolStripMenuItem _update;
        private readonly ToolStripMenuItem _showMac;
        private Action _installUpdate;
        private bool _hintShown;
        private Icon _current;

        /// <param name="showMac">Brings the AirPlay display back to the front; offered while a device is connected.</param>
        public TrayIcon(ReceiverController controller, Action open, Action exit, Func<bool> macDisplayOpen, Action showMac)
        {
            _controller = controller;

            _status = new ToolStripMenuItem { Enabled = false };
            var openItem = new ToolStripMenuItem("Mở MacExtend", null, (_, __) => open());
            openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
            _toggle = new ToolStripMenuItem("", null, (_, __) => Toggle());
            _restart = new ToolStripMenuItem("Khởi động lại bộ nhận", null, (_, __) => _controller.RestartAsync());
            var exitItem = new ToolStripMenuItem("Thoát MacExtend", null, (_, __) => exit());
            _update = new ToolStripMenuItem("", null, (_, __) => _installUpdate?.Invoke()) { Visible = false };
            _showMac = new ToolStripMenuItem("Hiện màn hình AirPlay", null, (_, __) => showMac()) { Visible = false };

            _menu = new ContextMenuStrip { ShowImageMargin = false, Padding = new Padding(0, 4, 0, 4) };
            _menu.Items.AddRange(new ToolStripItem[]
            {
                _status, new ToolStripSeparator(), _showMac, openItem, _toggle, _restart, _update, new ToolStripSeparator(), exitItem,
            });
            foreach (ToolStripItem item in _menu.Items)
            {
                item.Padding = new Padding(8, 5, 24, 5);
            }

            _icon = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
            _icon.MouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                // while a device is streaming, a click on the icon returns to its display
                if (macDisplayOpen()) showMac();
                else open();
            };

            _controller.StateChanged += Update;
            ThemeManager.Changed += Update;
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

            var tip = "MacExtend: " + text;
            _icon.Text = tip.Length > 63 ? tip.Substring(0, 62) + "…" : tip;
            _status.Text = text;
            _toggle.Text = phase == ReceiverPhase.Stopped || phase == ReceiverPhase.Failed ? "Bắt đầu" : "Dừng";
            _restart.Visible = phase != ReceiverPhase.Stopped;
            _showMac.Visible = phase == ReceiverPhase.Connected;
            _menu.Renderer = new ModernMenuRenderer(!WindowsTheme.SystemUsesLightTheme);
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

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MacExtend.Tray
{
    /// <summary>Flat tray menu in the app's palette instead of the default WinForms gradient look.</summary>
    internal sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Palette _p;

        public ModernMenuRenderer(bool dark) : base(new MenuColors(new Palette(dark)))
        {
            _p = new Palette(dark);
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(_p.Background)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(_p.Border))
            {
                var r = e.AffectedBounds;
                e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(r, 4))
            using (var b = new SolidBrush(_p.Hover))
            {
                e.Graphics.FillPath(b, path);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? _p.Text : _p.Secondary;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using (var pen = new Pen(_p.Border)) e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class Palette
        {
            public Palette(bool dark)
            {
                // same values as Themes/Colors.*.xaml (Sheet.Background, Sheet.Border, Text.*, Control.Pressed)
                Background = dark ? Color.FromArgb(0x22, 0x25, 0x29) : Color.FromArgb(0xFF, 0xFF, 0xFF);
                Border = dark ? Color.FromArgb(0x2E, 0x32, 0x38) : Color.FromArgb(0xE2, 0xE6, 0xEB);
                Hover = dark ? Color.FromArgb(0x38, 0x3D, 0x44) : Color.FromArgb(0xEB, 0xEE, 0xF2);
                Text = dark ? Color.FromArgb(0xEC, 0xEE, 0xF1) : Color.FromArgb(0x16, 0x19, 0x1D);
                Secondary = dark ? Color.FromArgb(0xA0, 0xA8, 0xB3) : Color.FromArgb(0x5B, 0x64, 0x70);
            }

            public Color Background { get; }
            public Color Border { get; }
            public Color Hover { get; }
            public Color Text { get; }
            public Color Secondary { get; }
        }

        private sealed class MenuColors : ProfessionalColorTable
        {
            private readonly Palette _p;

            public MenuColors(Palette p)
            {
                _p = p;
                UseSystemColors = false;
            }

            public override Color ToolStripDropDownBackground => _p.Background;
            public override Color ImageMarginGradientBegin => _p.Background;
            public override Color ImageMarginGradientMiddle => _p.Background;
            public override Color ImageMarginGradientEnd => _p.Background;
            public override Color MenuBorder => _p.Border;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuItemSelected => _p.Hover;
            public override Color SeparatorDark => _p.Border;
            public override Color SeparatorLight => _p.Border;
        }
    }
}

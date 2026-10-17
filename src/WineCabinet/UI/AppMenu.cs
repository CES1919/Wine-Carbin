using System.Drawing.Drawing2D;

namespace WineCabinet;

internal static class AppMenu
{
    public static ContextMenuStrip Create()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(250, 250, 252),
            ForeColor = Color.FromArgb(29, 29, 31),
            Font = new Font("Segoe UI Variable Text", 9.25f, FontStyle.Regular),
            ShowImageMargin = false,
            Padding = new Padding(6),
            Renderer = new MenuRenderer()
        };
        menu.Opened += (_, _) => ApplyRoundedRegion(menu);
        menu.ItemAdded += (_, e) =>
        {
            if (e.Item is null) return;
            e.Item.Padding = new Padding(12, 6, 16, 6);
            if (e.Item is ToolStripMenuItem item)
            {
                item.DropDown.BackColor = menu.BackColor;
                item.DropDown.Font = menu.Font;
                item.DropDown.Padding = menu.Padding;
                item.DropDown.Renderer = menu.Renderer;
                item.DropDown.Opened += (_, _) => ApplyRoundedRegion(item.DropDown);
            }
        };
        return menu;
    }

    private static void ApplyRoundedRegion(ToolStrip strip)
    {
        if (strip.Width < 2 || strip.Height < 2) return;
        using var path = RoundedRectangle(new Rectangle(0, 0, strip.Width, strip.Height), 10);
        strip.Region = new Region(path);
    }

    private static GraphicsPath RoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) => RoundedEdges = true;

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRectangle(new Rectangle(3, 2, e.Item.Width - 6, e.Item.Height - 4), 7);
            using var brush = new SolidBrush(Color.FromArgb(0, 122, 255));
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Color.White : Color.FromArgb(29, 29, 31);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(220, 220, 224));
            e.Graphics.DrawLine(pen, 10, e.Item.Height / 2, e.Item.Width - 10, e.Item.Height / 2);
        }
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(250, 250, 252);
        public override Color MenuBorder => Color.FromArgb(210, 210, 215);
        public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
    }
}

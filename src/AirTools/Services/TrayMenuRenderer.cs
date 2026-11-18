using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AirTools.Services;

/// <summary>Win11-style tray menu: flat surface, rounded hover highlight, hairline separators, theme aware.</summary>
internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    public bool Dark { get; set; } = true;
    private Color Surface => Dark ? Color.FromArgb(44, 44, 46) : Color.FromArgb(249, 249, 251);
    private Color Hover => Dark ? Color.FromArgb(62, 62, 66) : Color.FromArgb(234, 234, 238);
    private Color TextColor => Dark ? Color.FromArgb(242, 242, 245) : Color.FromArgb(28, 28, 32);
    private Color Line => Dark ? Color.FromArgb(70, 70, 74) : Color.FromArgb(222, 222, 226);

    public TrayMenuRenderer() { RoundedEdges = true; }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Surface);
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Line);
        e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var rect = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(rect, 5);
        using var brush = new SolidBrush(Hover);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = TextColor;
        e.TextFormat |= TextFormatFlags.VerticalCenter;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Line);
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath(); int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
}

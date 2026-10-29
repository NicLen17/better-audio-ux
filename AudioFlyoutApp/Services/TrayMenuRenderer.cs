using System.Drawing;
using Forms = System.Windows.Forms;

namespace BetterAudio.Services;

internal sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    private static readonly Color Surface = Color.FromArgb(23, 40, 45);
    private static readonly Color Mint = Color.FromArgb(150, 230, 201);
    public TrayMenuRenderer() { RoundedEdges = false; }
    public static void Apply(Forms.ContextMenuStrip menu)
    {
        menu.Renderer = new TrayMenuRenderer();
        menu.BackColor = Surface;
        menu.ForeColor = Color.FromArgb(227, 245, 238);
        menu.ShowImageMargin = false;
        menu.Padding = new Forms.Padding(5);
    }
    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }
    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Mint);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }
    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        using var brush = new SolidBrush(Color.FromArgb(52, 87, 78));
        e.Graphics.FillRectangle(brush, new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2));
    }
    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Color.FromArgb(227, 245, 238) : Color.FromArgb(145, 178, 167);
        base.OnRenderItemText(e);
    }
    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(65, 103, 92));
        e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
    }
}

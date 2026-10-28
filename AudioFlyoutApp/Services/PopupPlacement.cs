using System;
using System.Drawing;

namespace BetterAudio.Services;

public readonly record struct PopupPosition(int X, int Y, double OriginX, bool Above);

public static class PopupPlacement
{
    public static PopupPosition Calculate(Rectangle work, Point anchor, int width, int height, int gap = 10)
    {
        var above = anchor.Y > work.Top + work.Height / 2;
        var x = Math.Clamp(anchor.X - width / 2, work.Left, Math.Max(work.Left, work.Right - width));
        var y = above ? anchor.Y - height - gap : anchor.Y + gap;
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        return new(x, y, Math.Clamp((anchor.X - x) / (double)Math.Max(1, width), 0, 1), above);
    }
}

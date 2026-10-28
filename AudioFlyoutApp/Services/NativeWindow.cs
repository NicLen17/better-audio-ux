using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace BetterAudio.Services;

internal static class NativeWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    public static System.Drawing.Point Cursor { get { GetCursorPos(out var point); return new(point.X, point.Y); } }

    public static void SetInteractive(Window window, bool interactive)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new IntPtr(interactive ? style & ~0x08000000L : style | 0x08000000L));
        window.ShowActivated = interactive;
    }

    public static PopupPosition Position(Window window, System.Drawing.Point anchor)
    {
        var work = Forms.Screen.FromPoint(anchor).WorkingArea;
        var monitor = MonitorFromPoint(new Point { X = anchor.X, Y = anchor.Y }, 2);
        GetDpiForMonitor(monitor, 0, out var dpi, out _);
        var scale = (dpi == 0 ? 96 : dpi) / 96.0;
        var width = (int)Math.Round(window.Width * scale);
        var height = (int)Math.Round(window.Height * scale);
        var position = PopupPlacement.Calculate(work, anchor, width, height, (int)(8 * scale));
        SetWindowPos(new WindowInteropHelper(window).EnsureHandle(), new IntPtr(-1), position.X, position.Y, width, height, 0x0010);
        return position;
    }
}

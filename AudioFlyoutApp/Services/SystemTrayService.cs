using System;
using System.Drawing;
using System.Windows.Threading;
using BetterAudio.UI.Flyout;
using Forms = System.Windows.Forms;

namespace BetterAudio.Services;

public sealed class SystemTrayService : IDisposable
{
    private readonly NativeTrayIcon _icon;
    private readonly Icon _image;
    private readonly FlyoutWindow _window;
    private readonly DispatcherTimer _hover;
    private readonly DispatcherTimer _leave;
    private readonly Forms.ContextMenuStrip _menu;
    private Point _anchor;
    private DateTime _outsideSince;
    private DateTime _dismissedAt;
    private bool _contextOpen;

    public SystemTrayService(FlyoutWindow window)
    {
        _window = window;
        _hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _leave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        using var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/audio.ico")).Stream;
        _image = new Icon(resource);
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("Abrir Better Audio", null, (_, _) => Show(true));
        _menu.Items.Add("Mezclador de volumen", null, (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true }));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Salir", null, (_, _) => System.Windows.Application.Current.Shutdown());
        _menu.Opening += (_, _) => { _contextOpen = true; _hover.Stop(); _window.Dismiss(); };
        _menu.Closed += (_, _) => { _contextOpen = false; _dismissedAt = DateTime.UtcNow; };
        _icon = new NativeTrayIcon(_image);
        _icon.ContextRequested += (_, _) => { _icon.ActivateContextMenu(); _menu.Show(NativeWindow.Cursor); };
        _hover.Tick += (_, _) =>
        {
            _hover.Stop();
            var point = NativeWindow.Cursor;
            if (!_contextOpen && Math.Abs(point.X - _anchor.X) < 24 && Math.Abs(point.Y - _anchor.Y) < 24) Show(false);
        };
        _icon.Hover += (_, _) =>
        {
            if (_contextOpen || _window.IsVisible || _window.IsOpening || DateTime.UtcNow - _dismissedAt < TimeSpan.FromMilliseconds(800)) return;
            _anchor = NativeWindow.Cursor;
            if (!_hover.IsEnabled) _hover.Start();
        };
        _icon.Selected += (_, _) =>
        {
            _hover.Stop();
            if (_window.IsVisible && _window.IsPinned) _window.Dismiss();
            else if (DateTime.UtcNow - _dismissedAt > TimeSpan.FromMilliseconds(250)) Show(true);
        };
        _leave.Tick += (_, _) => CheckPointer();
        _window.Dismissed += WindowDismissed;
    }

    private void WindowDismissed(object? sender, EventArgs e) { _dismissedAt = DateTime.UtcNow; _leave.Stop(); }
    public void Show(bool interactive)
    {
        _hover.Stop();
        var rect = _icon.Bounds;
        _anchor = rect is { } bounds ? new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2) : NativeWindow.Cursor;
        _window.Present(interactive, _anchor);
        _outsideSince = DateTime.MaxValue;
        _leave.Start();
    }
    private void CheckPointer()
    {
        if (_window.IsOpening) return;
        if (!_window.IsVisible) { _leave.Stop(); return; }
        if (_window.IsPinned) return;
        var cursor = NativeWindow.Cursor;
        var topLeft = _window.PointToScreen(new System.Windows.Point(0, 0));
        var bottomRight = _window.PointToScreen(new System.Windows.Point(_window.ActualWidth, _window.ActualHeight));
        var inWindow = cursor.X >= topLeft.X - 10 && cursor.X <= bottomRight.X + 10 && cursor.Y >= topLeft.Y - 10 && cursor.Y <= bottomRight.Y + 10;
        var inTray = Math.Abs(cursor.X - _anchor.X) < 28 && Math.Abs(cursor.Y - _anchor.Y) < 28;
        if (inWindow || inTray) _outsideSince = DateTime.MaxValue;
        else if (_outsideSince == DateTime.MaxValue) _outsideSince = DateTime.UtcNow;
        else if (DateTime.UtcNow - _outsideSince > TimeSpan.FromMilliseconds(550)) _window.Dismiss();
    }
    public void Dispose()
    {
        _hover.Stop(); _leave.Stop(); _window.Dismissed -= WindowDismissed;
        _icon.Dispose(); _menu.Dispose(); _image.Dispose();
    }
}

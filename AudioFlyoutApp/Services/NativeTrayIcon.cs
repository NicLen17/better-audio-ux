using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BetterAudio.Services;

// A GUID-owned Shell icon exposes its actual rectangle without private framework reflection.
internal sealed class NativeTrayIcon : IDisposable
{
    // Explorer associates GUID icons with executable paths. Test and portable builds
    // must not register the same GUID from different executables.
    private static readonly Guid Identity = new(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes((Environment.ProcessPath ?? "BetterAudio").ToUpperInvariant())).AsSpan(0, 16));
    private const int Callback = 0x803D;
    private readonly HwndSource _source;
    private readonly Icon _image;
    private readonly uint _taskbarCreated;
    private readonly Guid _identity;
    private bool _disposed;
    public event EventHandler? Hover;
    public event EventHandler? Selected;
    public event EventHandler? ContextRequested;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size; public IntPtr Window; public uint Id; public uint Flags; public uint CallbackMessage; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State; public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Identifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIconW(uint message, ref NotifyData data);
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref Identifier identifier, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    public NativeTrayIcon(Icon image, Guid? identity = null)
    {
        _image = image;
        _identity = identity ?? Identity;
        _source = new HwndSource(new HwndSourceParameters("Better Audio Â· Tray") { WindowStyle = 0, Width = 0, Height = 0 });
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _source.AddHook(WindowProc);
        try { AddIcon(); }
        catch { _source.Dispose(); throw; }
    }
    private NotifyData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyData>(), Window = _source.Handle, Id = 1,
        Flags = 0x01 | 0x02 | 0x04 | 0x20, CallbackMessage = Callback, Icon = _image.Handle,
        Tip = "Better Audio", Info = "", InfoTitle = "", Guid = _identity, Version = 4
    };
    private void AddIcon()
    {
        var data = Data();
        if (!Shell_NotifyIconW(0, ref data)) throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo crear el icono de audio.");
        if (!Shell_NotifyIconW(4, ref data))
        { Shell_NotifyIconW(2, ref data); throw new InvalidOperationException("Windows no aceptÃ³ el protocolo de bandeja."); }
    }
    public Rectangle? Bounds
    {
        get
        {
            var identifier = new Identifier { Size = (uint)Marshal.SizeOf<Identifier>(), Window = _source.Handle, Id = 1, Guid = _identity };
            return Shell_NotifyIconGetRect(ref identifier, out var rect) == 0 && rect.Right > rect.Left
                ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
        }
    }
    public void ActivateContextMenu() => SetForegroundWindow(_source.Handle);
    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed) return IntPtr.Zero;
        if ((uint)message == _taskbarCreated)
        {
            try { AddIcon(); } catch (Exception ex) { Log.Write("Could not restore tray icon after Explorer restart", ex); }
        }
        if (message == Callback)
        {
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case 0x0200: case 0x0406: Hover?.Invoke(this, EventArgs.Empty); break;
                case 0x0400: case 0x0401: Selected?.Invoke(this, EventArgs.Empty); break;
                case 0x007B: ContextRequested?.Invoke(this, EventArgs.Empty); break;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = Data(); Shell_NotifyIconW(2, ref data);
        _source.RemoveHook(WindowProc); _source.Dispose();
    }
}

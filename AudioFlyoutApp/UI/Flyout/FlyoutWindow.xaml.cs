using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using BetterAudio.Core.Models;
using BetterAudio.Services;
using BetterAudio.UI.ViewModels;

namespace BetterAudio.UI.Flyout;

public partial class FlyoutWindow : Window
{
    public FlyoutViewModel ViewModel { get; }
    public bool IsPinned { get; private set; }
    public bool KeepOpen { get; private set; }
    public event EventHandler? Dismissed;
    private bool _allowClose;
    private int _carouselWheel;
    private int _volumeWheel;
    private System.Drawing.Point? _lastAnchor;
    public bool IsOpening { get; private set; }
    private bool _requestedInteractive;
    private int _presentationVersion;

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Height = viewModel.HudHeight;
        PreviewMouseDown += (_, _) =>
        {
            if (IsPinned) return;
            IsPinned = true;
            NativeWindow.SetInteractive(this, true);
            if (!IsActive) Activate();
        };
        viewModel.PropertyChanged += ViewModel_Changed;
        SizeChanged += (_, _) =>
        {
            if (IsVisible && _lastAnchor is { } anchor)
            {
                if (Math.Abs(Height - ViewModel.HudHeight) > .1) Height = ViewModel.HudHeight;
                if (Math.Abs(Width - 408) > .1) Width = 408;
                var placement = NativeWindow.Position(this, anchor);
                HudSurface.RenderTransformOrigin = new Point(placement.OriginX, placement.Above ? 1 : 0);
            }
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowProc);
    }

    public async void Present(bool interactive, System.Drawing.Point anchor)
    {
        _lastAnchor = anchor;
        if (IsOpening) { _requestedInteractive |= interactive; return; }
        if (IsVisible)
        {
            if (interactive) { IsPinned = true; NativeWindow.SetInteractive(this, true); if (!IsActive) Activate(); Focus(); }
            return;
        }
        IsOpening = true;
        _requestedInteractive = interactive;
        var version = ++_presentationVersion;
        // Finish endpoint selection while hidden. Never show an opaque frame and
        // then restart its opacity after an asynchronous audio refresh.
        await ViewModel.RefreshAsync(focusDefault: true);
        if (version != _presentationVersion) return;
        IsPinned = _requestedInteractive;
        BeginAnimation(OpacityProperty, null);
        RevealScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        RevealScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        RevealOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        Opacity = 1;
        HudSurface.BeginAnimation(OpacityProperty, null);
        HudSurface.Opacity = SystemParameters.ClientAreaAnimation ? 0 : 1;
        RevealScale.ScaleX = RevealScale.ScaleY = 1;
        RevealOffset.Y = 0;
        NativeWindow.SetInteractive(this, IsPinned);
        // HWND creation can report its provisional monitor-sized bounds back to
        // WPF. Reapply the HUD dimensions before the first visible frame.
        Height = ViewModel.HudHeight;
        Width = 408;
        NativeWindow.Position(this, anchor);
        Show();
        // Reposition after Show has applied the destination monitor's DPI.
        var placement = NativeWindow.Position(this, anchor);
        HudSurface.RenderTransformOrigin = new Point(placement.OriginX, placement.Above ? 1 : 0);
        if (IsPinned) { if (!IsActive) Activate(); Focus(); }
        IsOpening = false;
        _carouselWheel = 0; _volumeWheel = 0;
        if (SystemParameters.ClientAreaAnimation)
            HudSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private IntPtr WindowProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021 && !IsPinned) // WM_MOUSEACTIVATE: intentional click pins the preview.
        {
            IsPinned = true;
            NativeWindow.SetInteractive(this, true);
        }
        if (message == 0x020E) // WM_MOUSEHWHEEL, including horizontal mouse/trackpad input.
        {
            if (ViewModel.IsMixerOpen) return IntPtr.Zero;
            _carouselWheel += (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            while (Math.Abs(_carouselWheel) >= 120)
            {
                var direction = Math.Sign(_carouselWheel);
                ViewModel.MoveSelection(direction); _carouselWheel -= direction * 120;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dismiss()
    {
        _presentationVersion++;
        IsOpening = false;
        if (!IsVisible) return;
        Hide();
        HudSurface.BeginAnimation(OpacityProperty, null);
        HudSurface.Opacity = 0;
        IsPinned = false; KeepOpen = false; UpdatePin();
        _ = ViewModel.CloseMixerAsync();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        KeepOpen = !KeepOpen;
        UpdatePin();
    }
    private void UpdatePin()
    {
        PinShape.Fill = KeepOpen ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 44, 40)) : System.Windows.Media.Brushes.Transparent;
        PinButton.ToolTip = KeepOpen ? "Desfijar panel: cerrar al perder el foco" : "Fijar panel: mantener abierto al usar otra aplicación";
        System.Windows.Automation.AutomationProperties.SetItemStatus(PinButton, KeepOpen ? "Fijado" : "Sin fijar");
    }
    private void MixerList_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer list) { list.ScrollToVerticalOffset(list.VerticalOffset - e.Delta * .4); e.Handled = true; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Dismiss();
    private void Footer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Border clips its background, but not children. Keep the section fills
        // inside the rounded outside corners while the inside edge stays flat.
        var surface = (Border)sender;
        var width = e.NewSize.Width;
        var height = e.NewSize.Height;
        var radius = Math.Min(43, Math.Min(width / 2, height));
        var clip = new System.Windows.Media.StreamGeometry();
        using (var path = clip.Open())
        {
            path.BeginFigure(new Point(0, 0), true, true);
            path.LineTo(new Point(width, 0), true, false);
            path.LineTo(new Point(width, height - radius), true, false);
            path.ArcTo(new Point(width - radius, height), new Size(radius, radius), 0, false, System.Windows.Media.SweepDirection.Clockwise, true, false);
            path.LineTo(new Point(radius, height), true, false);
            path.ArcTo(new Point(0, height - radius), new Size(radius, radius), 0, false, System.Windows.Media.SweepDirection.Clockwise, true, false);
        }
        clip.Freeze();
        surface.Clip = clip;
    }
    private void Window_Deactivated(object? sender, EventArgs e) { if (IsPinned && !KeepOpen && !IsOpening) Dismiss(); }
    private void Previous_Click(object sender, RoutedEventArgs e) => ViewModel.MoveSelection(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => ViewModel.MoveSelection(1);
    private async void Device_Click(object sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: AudioDeviceModel device }) await ViewModel.CommitAsync(device); }
    private async void Mute_Click(object sender, RoutedEventArgs e) => await ViewModel.ToggleMuteAsync();
    private async void Retry_Click(object sender, RoutedEventArgs e) => await ViewModel.RefreshAsync();
    private async void Mixer_Click(object sender, RoutedEventArgs e) => await ViewModel.OpenMixerAsync();
    private void AudioSettings_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:sound");
    private void MixerSettings_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:apps-volume");
    private async void Back_Click(object sender, RoutedEventArgs e) => await ViewModel.CloseMixerAsync();
    private async void SpatialToggle_Click(object sender, RoutedEventArgs e)
    { if (sender is CheckBox checkbox) await ViewModel.SetSpatialEnabledAsync(checkbox.IsChecked == true); }
    private async void SessionMute_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: MixerSessionModel session }) await ViewModel.ToggleSessionMuteAsync(session); }
    private void SessionVolume_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MixerSessionModel session })
        { session.Volume += Math.Sign(e.Delta) * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 2); e.Handled = true; }
    }
    private void Spatial_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDevice is { } device)
            OpenSettings("ms-settings:sound-properties?endpointId=" + Uri.EscapeDataString(device.Id));
    }
    private void OpenSettings(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); Dismiss(); }
        catch (Exception ex) { Log.Write("Could not open Windows Settings", ex); System.Windows.MessageBox.Show("No se pudo abrir Configuración de Windows.", "Better Audio"); }
    }
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (!IsPinned || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        var session = (Keyboard.FocusedElement as FrameworkElement)?.DataContext as MixerSessionModel;
        if (ViewModel.IsMixerOpen && e.Key is Key.Left or Key.Right or Key.Enter) return;
        if (e.Key == Key.Enter && Keyboard.FocusedElement is Button focused && focused.DataContext is not AudioDeviceModel) return;
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter or Key.M or Key.Escape)) return;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Left: ViewModel.MoveSelection(-1); break;
            case Key.Right: ViewModel.MoveSelection(1); break;
            case Key.Up: if (session is not null) session.Volume += Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 2; else ViewModel.AdjustVolume(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 2); break;
            case Key.Down: if (session is not null) session.Volume -= Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 2; else ViewModel.AdjustVolume(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -10 : -2); break;
            case Key.Enter:
                await ViewModel.CommitAsync(); break;
            case Key.M: if (session is not null) await ViewModel.ToggleSessionMuteAsync(session); else await ViewModel.ToggleMuteAsync(); break;
            case Key.Escape: if (ViewModel.IsMixerOpen) await ViewModel.CloseMixerAsync(); else Dismiss(); break;
            default: return;
        }
    }
    private void Carousel_Wheel(object sender, MouseWheelEventArgs e)
    {
        _carouselWheel += e.Delta;
        while (Math.Abs(_carouselWheel) >= 120)
        {
            var direction = Math.Sign(_carouselWheel);
            ViewModel.MoveSelection(-direction); _carouselWheel -= direction * 120;
        }
        e.Handled = true;
    }
    private void Volume_Wheel(object sender, MouseWheelEventArgs e)
    {
        _volumeWheel += e.Delta;
        while (Math.Abs(_volumeWheel) >= 120)
        {
            var direction = Math.Sign(_volumeWheel);
            ViewModel.AdjustVolume(direction * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 2));
            _volumeWheel -= direction * 120;
        }
        e.Handled = true;
    }
    private void ViewModel_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FlyoutViewModel.HudHeight)) Height = ViewModel.HudHeight;
        if (e.PropertyName == nameof(FlyoutViewModel.Volume) && IsVisible && SystemParameters.ClientAreaAnimation)
            VolumeLabel.BeginAnimation(OpacityProperty, new DoubleAnimation(.55, 1, TimeSpan.FromMilliseconds(130)));
        if (e.PropertyName == nameof(FlyoutViewModel.IsMixerOpen) && IsVisible && SystemParameters.ClientAreaAnimation)
        {
            var view = ViewModel.IsMixerOpen ? MixerView : OutputView;
            view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            ((System.Windows.Media.TranslateTransform)view.RenderTransform).BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation(ViewModel.IsMixerOpen ? 10 : -10, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose) { e.Cancel = true; Dismiss(); }
        base.OnClosing(e);
    }
    public void CloseForExit() { _allowClose = true; ViewModel.PropertyChanged -= ViewModel_Changed; Close(); }
}

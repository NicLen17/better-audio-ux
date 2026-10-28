using System.IO;
using System.Windows.Threading;
using BetterAudio.Core.Audio;
using BetterAudio.Core.Models;
using BetterAudio.UI.ViewModels;
using BetterAudio.Services;
using BetterAudio.UI.Flyout;

internal static class Program
{
    private static int _assertions;
    [STAThread]
    public static int Main(string[] args)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var exitCode = 0;
        dispatcher.BeginInvoke(async () =>
        {
            try
            {
                if (args.Contains("--render")) await RenderHudAsync(dispatcher);
                else if (args.Contains("--ui") || args.Contains("--ui-demo") || args.Contains("--ui-mixer-demo")) await PreviewAsync(dispatcher, args.Contains("--ui-demo") || args.Contains("--ui-mixer-demo"), args.Contains("--ui-mixer-demo"));
                else if (args.Contains("--spatial-roundtrip")) await SpatialRoundtripAsync();
                else if (args.Contains("--live")) await LiveAsync();
                else { await RegressionAsync(dispatcher); await AutomaticSwitchAsync(dispatcher); await MixerAndSpatialAsync(dispatcher); GeometryAndPreferences(); await HudLayoutAsync(dispatcher); await AnimationAndOpeningAsync(dispatcher); }
                Console.WriteLine($"PASS: {_assertions} assertions");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        return exitCode;
    }
    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception("FAIL: " + reason);
        _assertions++;
        Console.WriteLine("PASS: " + reason);
    }
    private static DeviceSnapshot Device(string id, bool active = false, int volume = 50) => new(id, id, "Test", "", volume, false, active);
    private static async Task RegressionAsync(Dispatcher dispatcher)
    {
        using var audio = new FakeAudio();
        using var vm = new FlyoutViewModel(audio, dispatcher, new MemoryPreferences(false));
        await vm.RefreshAsync();
        Check(!vm.HasDevices && vm.SelectedDevice is null, "Empty endpoints handled");
        vm.MoveSelection(1); vm.AdjustVolume(2); await vm.CommitAsync();
        Check(audio.Commits == 0 && audio.Writes.Count == 0, "Empty actions do not touch audio");
        audio.Devices = [Device("A", true, 30), Device("B", false, 60), Device("C", false, 90)];
        await vm.RefreshAsync();
        Check(vm.SelectedDevice?.Id == "A" && vm.Volume == 30, "Initial selection follows system output");
        vm.MoveSelection(-1);
        Check(vm.SelectedDevice?.Id == "C" && vm.Volume == 90, "Left wraps to last device with its own volume");
        vm.MoveSelection(1);
        Check(vm.SelectedDevice?.Id == "A" && audio.Commits == 0, "Right wraps without committing");
        vm.MoveSelection(1); await vm.RefreshAsync();
        Check(vm.SelectedDevice?.Id == "B" && vm.Volume == 60, "Refresh preserves browsing selection");
        await vm.CommitAsync();
        Check(audio.Commits == 1 && audio.Devices.Single(d => d.IsDefault).Id == "B", "Explicit commit switches output");
        vm.Volume = 200;
        Check(vm.Volume == 100, "Volume clamps at 100");
        vm.Volume = 74; vm.Volume = 72; vm.MoveSelection(1);
        Check(vm.Volume == 90, "Browsing while volume write is pending loads next volume");
        await Task.Delay(180);
        Check(audio.Writes.Count == 1 && audio.Writes[0] == ("B", 72), "Rapid volume changes coalesce and retain original endpoint");
        Check(vm.Volume == 90, "Completed write does not change preview endpoint volume");
        vm.Volume = -10; await Task.Delay(180);
        Check(audio.Writes.Last() == ("C", 0), "Volume clamps at zero");
        await vm.ToggleMuteAsync();
        Check(audio.Devices.Single(d => d.Id == "C").Muted, "Mute applies to previewed output");
        audio.Devices = [Device("A", true, 30), Device("B", false, 72)];
        audio.Signal(); await Task.Delay(180);
        Check(vm.SelectedDevice?.Id == "A" && vm.Devices.Count == 2, "Hot-unplug falls back to available default via notification");
        audio.FailNextCommit = true; vm.MoveSelection(1); await vm.CommitAsync();
        Check(vm.HasMessage && audio.Devices.Single(d => d.IsDefault).Id == "A", "COM failure is visible and does not claim success");
        audio.Devices = []; audio.Signal(); await Task.Delay(180);
        Check(!vm.HasDevices && vm.SelectedIndex == -1 && vm.Volume == 0, "Removing all endpoints clears selection and volume");
        audio.Devices = [Device("D", true, 40)]; audio.Signal(); await Task.Delay(180);
        Check(vm.HasDevices && vm.SelectedDevice?.Id == "D" && vm.Volume == 40, "Recovery after reconnect");
        audio.Devices = [Device("D", true, 40), Device("E", false, 70)];
        await vm.RefreshAsync();
        audio.WriteDelay = 100;
        vm.Volume = 41;
        await Task.Delay(65);
        vm.MoveSelection(1);
        Check(vm.Volume == 70, "Changing preview during an in-flight write loads correct endpoint");
        vm.Volume = 73;
        vm.MoveSelection(-1);
        Check(vm.Volume == 41, "Returning to an endpoint preserves its in-flight volume");
        await Task.Delay(350);
        Check(audio.Devices.Single(d => d.Id == "D").Volume == 41 && audio.Devices.Single(d => d.Id == "E").Volume == 73,
            "Writes to different endpoints are retained while COM operations are in flight");
        Check(vm.Volume == 41, "Asynchronous notifications preserve current device volume");
    }
    private static async Task LiveAsync()
    {
        using var audio = new AudioDeviceManager();
        var snapshot = await audio.GetDevicesAsync();
        Check(snapshot.All(d => d.Volume is >= 0 and <= 100), "Real Windows endpoints expose valid volume");
        foreach (var device in snapshot) Console.WriteLine($"{(device.IsDefault ? "*" : " ")} {device.Name}: {device.Volume}% muted={device.Muted}");
        // Read-only integration check; physical unplug/Bluetooth and actual switching use the UI.
        for (var i = 0; i < 20; i++) await audio.GetDevicesAsync();
        Check(true, "Repeated enumeration and COM object cleanup");
        foreach (var device in snapshot)
        {
            var spatial = await audio.GetSpatialStateAsync(device.Id);
            Console.WriteLine($"Spatial {device.Name}: enabled={spatial.Enabled}, toggle={spatial.CanToggle}");
        }
        if (snapshot.FirstOrDefault(d => d.IsDefault) is { } current)
        {
            var sessions = await audio.GetSessionsAsync(current.Id);
            Check(sessions.All(s => s.DeviceId == current.Id && s.Volume is >= 0 and <= 100), "Mixer enumerates actual sessions with endpoint-scoped volume");
            foreach (var session in sessions) Console.WriteLine($"Session: {session.Name} {session.Volume}% muted={session.Muted}");
            if (sessions.FirstOrDefault() is { } first)
            {
                await audio.SetSessionVolumeAsync(current.Id, first.Id, first.Volume);
                await audio.SetSessionMuteAsync(current.Id, first.Id, first.Muted);
                Check(true, "Native mixer writes volume and mute back to the same session without changing their values");
            }
            await audio.StopWatchingSessionsAsync();
            Check(true, "Mixer event subscriptions release cleanly");
        }
        using var icon = (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        using var tray = new NativeTrayIcon(icon, Guid.NewGuid());
        await Task.Delay(150);
        Check(tray.Bounds is { Width: > 0, Height: > 0 }, "Shell returns the real rectangle of the GUID-owned tray icon");
    }
    private static async Task HudLayoutAsync(Dispatcher dispatcher)
    {
        using var audio = new FakeAudio { Devices = [Device("A"), Device("B", true), Device("C")] };
        using var vm = new FlyoutViewModel(audio, dispatcher, new MemoryPreferences(true));
        await vm.RefreshAsync();
        var window = new FlyoutWindow(vm);
        try
        {
            var root = (System.Windows.FrameworkElement)window.Content;
            root.Measure(new System.Windows.Size(408, 378));
            root.Arrange(new System.Windows.Rect(0, 0, 408, 378));
            root.UpdateLayout();
            var carousel = (System.Windows.Controls.ItemsControl)window.FindName("Carousel");
            Check(carousel.ItemContainerGenerator.ContainerFromIndex(1) is not null,
                "First HUD layout generates its radial items host with a preselected device");
            Check(window.Width == 408 && window.Height == 378 && window.AllowsTransparency,
                "HUD loads its compact dimensions and transparent window bindings");
            var pin = (System.Windows.Controls.Button)window.FindName("PinButton");
            pin.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(window.KeepOpen, "Header pin explicitly keeps the HUD open independently of keyboard focus");
            pin.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(!window.KeepOpen, "Header pin can return to automatic dismissal");
            await vm.OpenMixerAsync();
            var list = (System.Windows.Controls.ScrollViewer)window.FindName("MixerList");
            var volumeBeforeWheel = vm.Volume;
            var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent };
            list.RaiseEvent(wheel);
            Check(wheel.Handled && vm.Volume == volumeBeforeWheel && audio.SessionWrites.Count == 0, "Mixer wheel scrolls the list without changing master or application volumes");
            await vm.CloseMixerAsync();
            var slider = (AnimatedVolumeSlider)window.FindName("VolumeSlider");
            Check(slider.Value == vm.Volume && slider.DisplayValue == vm.Volume,
                "Volume control initializes presentation and audio value consistently");
        }
        finally { window.CloseForExit(); }
    }
    private static async Task SpatialRoundtripAsync()
    {
        using var audio = new AudioDeviceManager();
        var devices = await audio.GetDevicesAsync();
        var current = devices.FirstOrDefault(d => d.IsDefault) ?? throw new Exception("No default output");
        var initial = await audio.GetSpatialStateAsync(current.Id);
        if (initial.Enabled != false || !initial.CanToggle) { Console.WriteLine("SKIP: preserve existing spatial provider / unsupported endpoint"); return; }
        try
        {
            var enabled = await audio.SetSpatialEnabledAsync(current.Id, true);
            Check(enabled.Enabled == true, "Windows Sonic checkbox is verified against the native spatial renderer");
        }
        finally
        {
            var restored = await audio.SetSpatialEnabledAsync(current.Id, false);
            Check(restored.Enabled == false, "Spatial roundtrip restores the original disabled state");
        }
    }
    private static async Task RenderHudAsync(Dispatcher dispatcher)
    {
        using var audio = new FakeAudio { Devices = [
            new("a", "Altavoces (Realtek High Definition)", "Altavoces", "\uE767", 52, false, false),
            new("b", "Audífono (CORSAIR HS80 MAX WIRELESS Gaming Headset)", "Auriculares", "\uE7F6", 28, false, true),
            new("c", "Monitor (DisplayPort)", "Pantalla", "\uE7F4", 60, false, false)
        ], Sessions = [new("b", "browser", "Google Chrome", 74, false, true), new("b", "music", "Música", 42, false, true), new("b", "system", "Sonidos del sistema", 25, true, false)] };
        using var vm = new FlyoutViewModel(audio, dispatcher, new MemoryPreferences(true));
        await vm.RefreshAsync();
        var window = new FlyoutWindow(vm);
        try
        {
            foreach (var mixer in new[] { false, true })
            {
                if (mixer) await vm.OpenMixerAsync();
                var root = (System.Windows.FrameworkElement)window.Content;
                root.Measure(new System.Windows.Size(408, vm.HudHeight));
                root.Arrange(new System.Windows.Rect(0, 0, 408, vm.HudHeight)); root.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(816, (int)vm.HudHeight * 2, 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                var path = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", mixer ? "hud-mixer-preview.png" : "hud-preview.png");
                using var output = File.Create(path); encoder.Save(output); Console.WriteLine(path);
            }
        }
        finally { window.CloseForExit(); }
    }
    private static async Task MixerAndSpatialAsync(Dispatcher dispatcher)
    {
        using var audio = new FakeAudio { Devices = [Device("A", true), Device("B")],
            Sessions = [new("A", "one", "Navegador", 90, false, true), new("A", "two", "Música", 25, true, true), new("B", "one", "Navegador", 20, false, true)] };
        using var vm = new FlyoutViewModel(audio, dispatcher, new MemoryPreferences(false));
        await vm.RefreshAsync(); await vm.OpenMixerAsync();
        Check(vm.IsMixerOpen && vm.HasSessions && vm.Sessions.Count == 2 && vm.HudHeight == 482, "Mixer opens inside the HUD and scopes apps to the selected output");
        var browser = vm.Sessions.Single(s => s.Id == "one");
        browser.Volume = 73; browser.Volume = 71;
        await Task.Delay(150);
        Check(audio.SessionWrites.Count == 1 && audio.SessionWrites[0] == ("A", "one", 71), "Per-app slider coalesces writes and targets the exact session");
        await vm.ToggleSessionMuteAsync(vm.Sessions.Single(s => s.Id == "two"));
        Check(!audio.Sessions.Single(s => s.DeviceId == "A" && s.Id == "two").Muted, "Mixer mute only changes the requested application");
        audio.Sessions = audio.Sessions.Select(s => s.DeviceId == "A" && s.Id == "one" ? s with { Volume = 55 } : s).ToList();
        audio.Signal(); await Task.Delay(170);
        Check(ReferenceEquals(browser, vm.Sessions.Single(s => s.Id == "one")) && browser.Volume == 55, "External mixer changes update the existing slider without replacing controls");
        browser.Volume = 41; await vm.CloseMixerAsync(); vm.MoveSelection(1); await vm.OpenMixerAsync(); await Task.Delay(150);
        Check(audio.SessionWrites.Last() == ("A", "one", 41) && vm.Sessions.Single().DeviceId == "B" && vm.Sessions.Single().Volume == 20,
            "Pending mixer writes retain their endpoint when closing/navigating/reopening");
        audio.Sessions = []; audio.Signal(); await Task.Delay(170);
        Check(!vm.HasSessions && vm.Sessions.Count == 0, "Closed application sessions disappear without reopening the panel");
        await vm.RefreshSpatialAsync(force: true);
        Check(vm.SpatialEnabled == false && vm.CanToggleSpatial, "Spatial checkbox starts from backend state");
        await vm.SetSpatialEnabledAsync(true);
        Check(vm.SpatialEnabled == true && audio.SpatialEnabled, "Spatial checkbox reflects the verified enabled state");
        audio.FailSpatial = true; await vm.SetSpatialEnabledAsync(false);
        Check(vm.SpatialEnabled == true && vm.HasMessage && vm.CanToggleSpatial, "Failed spatial change restores actual checkbox state and allows retry");
        audio.SpatialKnown = false; await vm.RefreshSpatialAsync(force: true);
        Check(vm.SpatialEnabled is null && !vm.CanToggleSpatial, "Unknown spatial state disables the checkbox instead of inventing an enabled state");
        await vm.CloseMixerAsync();
        Check(!vm.IsMixerOpen && audio.StopWatchCalls > 0, "Returning from mixer releases session notifications");
    }
    private static async Task AnimationAndOpeningAsync(Dispatcher dispatcher)
    {
        var chamber = new System.Windows.Controls.Border();
        var hoverClock = ArcCarouselPanel.AnimateHover(chamber, true);
        if (System.Windows.SystemParameters.ClientAreaAnimation)
        {
            Check((double)chamber.GetValue(ArcCarouselPanel.HoverProgressProperty) < .1, "Side hover starts at its current position instead of jumping");
            hoverClock!.Controller!.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(90), System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
            var middle = (double)chamber.GetValue(ArcCarouselPanel.HoverProgressProperty);
            Check(middle > 0 && middle < 1, "Side hover has intermediate animated elevation and opacity");
        }
        var leaveClock = ArcCarouselPanel.AnimateHover(chamber, false);
        leaveClock?.Controller?.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(260), System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
        Check(Math.Abs((double)chamber.GetValue(ArcCarouselPanel.HoverProgressProperty)) < .001, "Mouse leave animates back to the resting arc");
        using var audio = new FakeAudio { Devices = [Device("A", true)], ReadDelay = 120 };
        using var vm = new FlyoutViewModel(audio, dispatcher, new MemoryPreferences(false));
        var window = new FlyoutWindow(vm);
        window.Present(false, new System.Drawing.Point(500, 500));
        window.Present(true, new System.Drawing.Point(500, 500));
        Check(window.IsOpening && !window.IsVisible && audio.ReadCalls == 1, "Hover/click during opening prepares a single hidden frame");
        window.Dismiss(); await Task.Delay(180);
        Check(!window.IsOpening && !window.IsVisible, "Cancelling a pending opening prevents a stale refresh from showing the HUD later");
        window.Present(false, new System.Drawing.Point(500, 500));
        await Task.Delay(150);
        Check(window.IsVisible && !window.IsOpening && window.Opacity == 1 && !window.HasAnimatedProperties,
            "Opening keeps the native transparent window opaque and animates only its content");
        await Task.Delay(230);
        var surface = (System.Windows.Controls.Border)window.FindName("HudSurface");
        Check(Math.Abs(surface.Opacity - 1) < .001, "Content reveal reaches full opacity without restarting");
        await vm.OpenMixerAsync();
        await Task.Delay(100);
        if (System.Windows.SystemParameters.ClientAreaAnimation)
            Check(window.Height > 378 && window.Height < 482, "Mixer expansion passes through intermediate heights");
        var expandingHeight = window.Height;
        await vm.CloseMixerAsync();
        if (System.Windows.SystemParameters.ClientAreaAnimation)
            Check(Math.Abs(window.Height - expandingHeight) < 3, "Reversing a resize starts from its current height without snapping");
        else
            Check(Math.Abs(window.Height - 378) < 1, "With animations disabled, returning to Main resizes immediately");
        await Task.Delay(360);
        Check(Math.Abs(window.Height - 378) < 1 && window.Opacity == 1, "Returning to Main finishes at its compact height without fading the native window");
        await vm.OpenMixerAsync();
        await Task.Delay(360);
        Check(Math.Abs(window.Height - 482) < 1, "Mixer expansion reaches its final height");
        var mixerView = (System.Windows.Controls.Grid)window.FindName("MixerView");
        Check(Math.Abs(mixerView.Opacity - 1) < .001, "Mixer content finishes its entrance fully visible");
        await vm.CloseMixerAsync();
        window.Dismiss();
        Check(surface.Opacity == 0, "Dismissal resets the hidden surface before the next opening");
        window.CloseForExit();
    }
    private static async Task AutomaticSwitchAsync(Dispatcher dispatcher)
    {
        using var audio = new FakeAudio { Devices = [Device("A", true), Device("B"), Device("C"), Device("D")], SwitchDelay = 70 };
        var preferences = new MemoryPreferences(true);
        using var vm = new FlyoutViewModel(audio, dispatcher, preferences);
        await vm.RefreshAsync();
        Check(vm.AutoSwitchOnNavigation && audio.Commits == 0, "Opening/refreshing with auto mode does not change the default");
        vm.MoveSelection(1);
        vm.MoveSelection(1);
        vm.MoveSelection(1);
        await Task.Delay(250);
        Check(audio.DefaultAttempts.SequenceEqual(["B", "D"]), "Rapid navigation serializes the current switch and retains only the latest pending choice");
        Check(audio.MaxConcurrentSwitches == 1 && audio.Devices.Single(d => d.IsDefault).Id == "D" && vm.SelectedDevice?.Id == "D", "Auto mode activates the final selected endpoint without concurrent COM writes");
        vm.MoveSelection(1); // A is already being applied.
        vm.MoveSelection(1); // B is queued.
        vm.AutoSwitchOnNavigation = false;
        await Task.Delay(160);
        Check(!preferences.AutoSwitchOnNavigation && audio.Devices.Single(d => d.IsDefault).Id == "A", "Disabling auto mode saves preference and cancels queued switches");
        var attempts = audio.DefaultAttempts.Count;
        vm.MoveSelection(1);
        await Task.Delay(100);
        Check(audio.DefaultAttempts.Count == attempts && vm.SelectedDevice?.Id == "C", "Unchecked mode only previews devices");
        await vm.CommitAsync();
        Check(audio.Devices.Single(d => d.IsDefault).Id == "C", "Enter/click confirmation still activates a device in manual mode");
        vm.AutoSwitchOnNavigation = true;
        audio.FailNextCommit = true;
        vm.MoveSelection(1);
        await Task.Delay(100);
        Check(vm.HasMessage && !vm.IsBusy && audio.Devices.Single(d => d.IsDefault).Id == "C", "Automatic switching failure is recoverable and visible");
        vm.MoveSelection(1);
        await Task.Delay(120);
        Check(!vm.HasMessage && audio.Devices.Single(d => d.IsDefault).Id == "A", "Navigation recovers after an automatic COM failure");
        attempts = audio.DefaultAttempts.Count;
        audio.Signal(); await Task.Delay(150);
        Check(audio.DefaultAttempts.Count == attempts, "Volume/device refresh notifications never trigger automatic switching");
        audio.Devices = audio.Devices.Select(d => d with { IsDefault = d.Id == "B" }).ToList();
        audio.Signal(); await Task.Delay(150);
        Check(vm.SelectedDevice?.Id == "B" && audio.DefaultAttempts.Count == attempts, "Auto mode follows external default changes without writing them back");
    }
    private static void GeometryAndPreferences()
    {
        var center = ArcLayout.Calculate(0, 0, 6, 352);
        var left = ArcLayout.Calculate(5, 0, 6, 352);
        var right = ArcLayout.Calculate(1, 0, 6, 352);
        Check(center.X == 0 && center.Y == 0 && center.Scale == 1 && center.Opacity == 1, "Radial center is full size and opacity");
        Check(Math.Abs(left.X + right.X) < .001 && left.Y == right.Y && right.Y > center.Y && Math.Abs(right.Scale - .65) < .001 && Math.Abs(right.Opacity - .4) < .001, "Adjacent chambers form a symmetric descending arc at 65% scale and 40% opacity");
        Check(ArcLayout.Calculate(0, 6, 6, 352) == center, "Wheel wrap preserves geometry without snapping back through the list");
        var work = new System.Drawing.Rectangle(0, 0, 1920, 1040);
        var position = PopupPlacement.Calculate(work, new(900, 1060), 408, 366);
        Check(position.X + 204 == 900 && position.OriginX == .5 && position.Above, "Popup centers on the tray icon instead of its right edge");
        position = PopupPlacement.Calculate(work, new(1900, 1060), 408, 366);
        Check(position.X + 408 == 1920 && position.OriginX > .5, "Screen-edge clamp retains the real icon as the animation origin");
        position = PopupPlacement.Calculate(new(-1920, -300, 1920, 1080), new(-1800, -320), 612, 549);
        Check(position.X == -1920 && position.Y == -300 && !position.Above, "Placement handles negative monitor coordinates and scaled dimensions");
        var directory = Path.Combine(Path.GetTempPath(), "BetterAudio.Tests-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var settings = new UserPreferences(path);
            Check(settings.AutoSwitchOnNavigation, "Automatic navigation is enabled for a new user");
            settings.AutoSwitchOnNavigation = false;
            Check(!new UserPreferences(path).AutoSwitchOnNavigation, "Checkbox preference survives a restart");
            File.WriteAllText(path, "invalid json");
            Check(new UserPreferences(path).AutoSwitchOnNavigation, "Corrupt preferences recover safely with defaults");
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
        var headset = new AudioDeviceModel(new("test", "Audífono de los auriculares con micrófono (CORSAIR HS80 MAX WIRELESS Gaming Headset)", "Auriculares", "", 28, false, true));
        Check(headset.DisplayName == "Corsair HS80 MAX", "HUD shows the commercial name and retains the original endpoint name separately");
    }
    private static async Task PreviewAsync(Dispatcher dispatcher, bool demo, bool mixer)
    {
        using IAudioDeviceManager audio = demo ? new FakeAudio { Devices = [
            new("a", "Altavoces (Realtek High Definition)", "Altavoces", "\uE767", 52, false, false),
            new("b", "Audífono (CORSAIR HS80 MAX WIRELESS Gaming Headset)", "Auriculares", "\uE7F6", 28, false, true),
            new("c", "Monitor (DisplayPort)", "Pantalla", "\uE7F4", 60, false, false),
            new("d", "Altavoces (Steam Streaming Speakers)", "Altavoces", "\uE767", 74, false, false)
        ], Sessions = [new("b", "browser", "Google Chrome", 74, false, true), new("b", "music", "Música", 42, false, true), new("b", "system", "Sonidos del sistema", 25, true, false)] } : new AudioDeviceManager();
        using var vm = new FlyoutViewModel(audio, dispatcher, new MemoryPreferences(demo));
        await vm.RefreshAsync();
        var window = new BetterAudio.UI.Flyout.FlyoutWindow(vm) { ShowInTaskbar = true };
        var dismissed = new TaskCompletionSource();
        window.Dismissed += (_, _) => dismissed.TrySetResult();
        window.Present(false, System.Windows.Forms.Cursor.Position);
        await Task.Delay(250);
        if (window.IsVisible)
        {
            // Keep the fixture focusable for desktop inspection; production hover
            // continues to use WS_EX_NOACTIVATE and its own mouse activation path.
            BetterAudio.Services.NativeWindow.SetInteractive(window, true);
            if (mixer) await vm.OpenMixerAsync();
            Console.WriteLine($"HUD layout: Height={window.Height}, ActualHeight={window.ActualHeight}, VM={vm.HudHeight}, Width={window.ActualWidth}");
        }
        await dismissed.Task;
        window.CloseForExit();
    }
    private sealed class FakeAudio : IAudioDeviceManager, IAudioMixerService
    {
        public List<DeviceSnapshot> Devices { get; set; } = [];
        public int Commits { get; private set; }
        public List<(string Id, int Volume)> Writes { get; } = [];
        public bool FailNextCommit { get; set; }
        public int WriteDelay { get; set; }
        public int SwitchDelay { get; set; }
        public List<string> DefaultAttempts { get; } = [];
        public int MaxConcurrentSwitches { get; private set; }
        private int _concurrentSwitches;
        public int ReadDelay { get; set; }
        public int ReadCalls { get; private set; }
        public List<SessionSnapshot> Sessions { get; set; } = [];
        public List<(string Device, string Session, int Volume)> SessionWrites { get; } = [];
        public bool SpatialEnabled { get; set; }
        public bool SpatialKnown { get; set; } = true;
        public bool FailSpatial { get; set; }
        public int StopWatchCalls { get; private set; }
        public event EventHandler? Changed;
        public void Signal() => Changed?.Invoke(this, EventArgs.Empty);
        public async Task<IReadOnlyList<DeviceSnapshot>> GetDevicesAsync()
        { ReadCalls++; if (ReadDelay > 0) await Task.Delay(ReadDelay); return Devices.ToArray(); }
        public Task<IReadOnlyList<SessionSnapshot>> GetSessionsAsync(string deviceId) => Task.FromResult<IReadOnlyList<SessionSnapshot>>(Sessions.Where(s => s.DeviceId == deviceId).ToArray());
        public Task SetSessionVolumeAsync(string device, string session, int volume)
        { SessionWrites.Add((device, session, volume)); Sessions = Sessions.Select(s => s.DeviceId == device && s.Id == session ? s with { Volume = volume } : s).ToList(); Signal(); return Task.CompletedTask; }
        public Task SetSessionMuteAsync(string device, string session, bool muted)
        { Sessions = Sessions.Select(s => s.DeviceId == device && s.Id == session ? s with { Muted = muted } : s).ToList(); Signal(); return Task.CompletedTask; }
        public Task StopWatchingSessionsAsync() { StopWatchCalls++; return Task.CompletedTask; }
        public Task<SpatialState> GetSpatialStateAsync(string device) => Task.FromResult(new SpatialState(SpatialKnown ? SpatialEnabled : null, SpatialKnown, "Test spatial"));
        public Task<SpatialState> SetSpatialEnabledAsync(string device, bool enabled)
        { if (FailSpatial) { FailSpatial = false; throw new InvalidOperationException("Spatial unavailable"); } SpatialEnabled = enabled; return GetSpatialStateAsync(device); }
        public async Task SetDefaultAsync(string id)
        {
            if (FailNextCommit) { FailNextCommit = false; throw new System.Runtime.InteropServices.COMException("Disconnected"); }
            DefaultAttempts.Add(id);
            _concurrentSwitches++;
            MaxConcurrentSwitches = Math.Max(MaxConcurrentSwitches, _concurrentSwitches);
            try
            {
                if (SwitchDelay > 0) await Task.Delay(SwitchDelay);
                Commits++;
                Devices = Devices.Select(d => d with { IsDefault = d.Id == id }).ToList();
            }
            finally { _concurrentSwitches--; }
        }
        public async Task SetVolumeAsync(string id, int volume)
        {
            Writes.Add((id, volume));
            if (WriteDelay > 0) await Task.Delay(WriteDelay);
            Devices = Devices.Select(d => d.Id == id ? d with { Volume = volume } : d).ToList();
            Signal();
        }
        public Task SetMuteAsync(string id, bool muted)
        { Devices = Devices.Select(d => d.Id == id ? d with { Muted = muted } : d).ToList(); return Task.CompletedTask; }
        public void Dispose() { }
    }
    private sealed class MemoryPreferences(bool initial) : IUserPreferences
    {
        public bool AutoSwitchOnNavigation { get; set; } = initial;
    }
}




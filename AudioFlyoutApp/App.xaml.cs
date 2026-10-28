using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterAudio.Core.Audio;
using BetterAudio.Services;
using BetterAudio.UI.Flyout;
using BetterAudio.UI.ViewModels;

namespace BetterAudio;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private IAudioDeviceManager? _audio;
    private FlyoutViewModel? _viewModel;
    private FlyoutWindow? _window;
    private SystemTrayService? _tray;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUiException;
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Write("Background error", args.Exception); args.SetObserved(); };
        try
        {
            _mutex = new Mutex(true, "Local\\BetterAudio.App", out _ownsMutex);
            if (!_ownsMutex)
            {
                try { using var existing = EventWaitHandle.OpenExisting("Local\\BetterAudio.Show"); existing.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                Shutdown(); return;
            }
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\BetterAudio.Show");
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(() => _tray?.Show(true)), null, Timeout.Infinite, false);
            _audio = new AudioDeviceManager();
            _viewModel = new FlyoutViewModel(_audio, Dispatcher);
            _window = new FlyoutWindow(_viewModel);
            MainWindow = _window;
            _tray = new SystemTrayService(_window);
            await _viewModel.RefreshAsync();
            // Show once on startup so the new icon and controls can be discovered.
            _tray.Show(true);
            Log.Write("Better Audio started");
        }
        catch (Exception ex)
        {
            Log.Write("Startup failed", ex);
            System.Windows.MessageBox.Show("No se pudo iniciar Better Audio. Encontrarás el diagnóstico en %LOCALAPPDATA%\\BetterAudio\\app.log.", "Better Audio", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write("Unexpected UI error", e.Exception);
        // Fail visibly; swallowing arbitrary dispatcher exceptions can leave COM/UI state invalid.
        System.Windows.MessageBox.Show("Better Audio encontró un error y se cerrará. Podés volver a abrirlo.", "Better Audio", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; Shutdown(1);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null); _showEvent?.Dispose();
        _tray?.Dispose(); _viewModel?.Dispose(); _window?.CloseForExit();
        try { _audio?.Dispose(); } catch (Exception ex) { Log.Write("Audio shutdown", ex); }
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using BetterAudio.Core.Models;
using BetterAudio.Services;
using NAudio.CoreAudioApi;

namespace BetterAudio.Core.Audio;

// All COM objects are created, read, modified and released on this one apartment.
public sealed partial class AudioDeviceManager : IAudioDeviceManager, IAudioMixerService
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, MMDevice> _devices = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _notifications;
    private volatile bool _disposed;
    public event EventHandler? Changed;

    public AudioDeviceManager()
    {
        _thread = new Thread(() => { _ready.SetResult(Dispatcher.CurrentDispatcher); Dispatcher.Run(); })
        { IsBackground = true, Name = "Better Audio · Core Audio" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private async Task<T> RunAsync<T>(Func<T> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var dispatcher = await _ready.Task.ConfigureAwait(false);
        return await dispatcher.InvokeAsync(action).Task.ConfigureAwait(false);
    }

    public Task<IReadOnlyList<DeviceSnapshot>> GetDevicesAsync() => RunAsync<IReadOnlyList<DeviceSnapshot>>(() =>
    {
        if (_enumerator is null)
        {
            _enumerator = new MMDeviceEnumerator();
            _notifications = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
            _notifications.DeviceAdded += (_, _) => Notify();
            _notifications.DeviceRemoved += (_, _) => Notify();
            _notifications.DeviceStateChanged += (_, _) => Notify();
            _notifications.PropertyValueChanged += (_, _) => Notify();
            _notifications.DefaultDeviceChanged += (_, args) => { if (args.Flow == DataFlow.Render) Notify(); };
        }
        string? defaultId = null;
        if (_enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var current))
        { using (current) defaultId = current.ID; }

        var found = new HashSet<string>();
        var result = new List<DeviceSnapshot>();
        using var endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var candidate in endpoints)
        {
            MMDevice? owned = candidate;
            string? candidateId = null;
            try
            {
                var id = candidate.ID;
                candidateId = id;
                found.Add(id);
                if (!_devices.TryGetValue(id, out var device))
                {
                    device = candidate;
                    device.AudioEndpointVolume.OnVolumeNotification += VolumeChanged;
                    _devices.Add(id, device);
                    owned = null;
                }
                var (kind, glyph) = Describe(device);
                result.Add(new(id, device.FriendlyName, kind, glyph,
                    (int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100),
                    device.AudioEndpointVolume.Mute, id == defaultId));
            }
            catch (Exception ex) when (ex is COMException or CoreAudioException or InvalidOperationException)
            {
                Log.Write("Endpoint unavailable during enumeration", ex);
                // A reconnected endpoint may keep its ID but invalidate its old COM object.
                if (candidateId is not null && _devices.ContainsKey(candidateId)) Release(candidateId);
            }
            finally { owned?.Dispose(); }
        }
        foreach (var id in _devices.Keys.Where(id => !found.Contains(id)).ToArray()) Release(id);
        return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    });

    private static (string Kind, string Glyph) Describe(MMDevice device) => DescribeName(device.FriendlyName);

    private static (string, string) DescribeName(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("head") || lower.Contains("auricular") || lower.Contains("buds")) return ("Auriculares", "\uE7F6");
        if (lower.Contains("hdmi") || lower.Contains("display") || lower.Contains("nvidia") || lower.Contains("monitor")) return ("Pantalla · audio digital", "\uE7F4");
        if (lower.Contains("digital") || lower.Contains("spdif")) return ("Salida digital", "\uE950");
        return ("Altavoces · salida de audio", "\uE767");
    }

    private MMDevice Get(string id) => _devices.TryGetValue(id, out var device) && device.State == DeviceState.Active
        ? device : throw new InvalidOperationException("El dispositivo se desconectó. Elegí otra salida.");
    public Task SetDefaultAsync(string id) => RunAsync(() => { _ = Get(id); ComPolicyConfig.SetDefault(id); return true; });
    public Task SetVolumeAsync(string id, int volume) => RunAsync(() => { Get(id).AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0, 100) / 100f; return true; });
    public Task SetMuteAsync(string id, bool muted) => RunAsync(() => { Get(id).AudioEndpointVolume.Mute = muted; return true; });
    private void VolumeChanged(AudioVolumeNotificationData data) => Notify();
    private void Notify() { if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }

    private void Release(string id)
    {
        if (_watchedEndpoint == id) StopWatchingSessions();
        var device = _devices[id];
        try { device.AudioEndpointVolume.OnVolumeNotification -= VolumeChanged; }
        catch (Exception ex) when (ex is COMException or CoreAudioException) { Log.Write("Endpoint cleanup", ex); }
        device.Dispose();
        _devices.Remove(id);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var dispatcher = _ready.Task.GetAwaiter().GetResult();
        dispatcher.Invoke(() =>
        {
            StopWatchingSessions();
            foreach (var id in _devices.Keys.ToArray()) Release(id);
            if (_enumerator is not null)
            {
                _notifications?.Dispose();
                _enumerator.Dispose();
            }
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        });
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}

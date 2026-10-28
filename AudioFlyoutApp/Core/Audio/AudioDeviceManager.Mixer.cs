using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BetterAudio.Core.Models;
using BetterAudio.Services;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace BetterAudio.Core.Audio;

public sealed partial class AudioDeviceManager
{
    private string? _watchedEndpoint;
    private AudioSessionManager? _watchedManager;
    private readonly Dictionary<string, AudioSessionControl> _sessions = new();
    private SessionEvents? _sessionEvents;

    public Task<IReadOnlyList<SessionSnapshot>> GetSessionsAsync(string deviceId) => RunAsync<IReadOnlyList<SessionSnapshot>>(() =>
    {
        var device = Get(deviceId);
        if (_watchedEndpoint != deviceId)
        {
            var manager = device.AudioSessionManager;
            StopWatchingSessions();
            _watchedEndpoint = deviceId;
            _watchedManager = manager;
            _watchedManager.OnSessionCreated += SessionCreated;
            _sessionEvents = new SessionEvents(Notify);
        }
        _watchedManager!.RefreshSessions();
        var collection = _watchedManager.Sessions;
        var found = new HashSet<string>();
        var snapshots = new List<SessionSnapshot>();
        for (var index = 0; index < collection.Count; index++)
        {
            AudioSessionControl? owned = collection[index];
            try
            {
                if (owned.State == AudioSessionState.AudioSessionStateExpired) continue;
                var id = owned.GetSessionInstanceIdentifier;
                if (string.IsNullOrEmpty(id) || !found.Add(id)) continue;
                if (!_sessions.TryGetValue(id, out var session))
                {
                    session = owned;
                    session.RegisterEventClient(_sessionEvents!);
                    _sessions.Add(id, session);
                    owned = null;
                }
                snapshots.Add(new(deviceId, id, SessionName(session),
                    (int)Math.Round(session.SimpleAudioVolume.Volume * 100), session.SimpleAudioVolume.Mute,
                    session.State == AudioSessionState.AudioSessionStateActive, SessionIcon(session)));
            }
            catch (Exception ex) when (ex is COMException or CoreAudioException or InvalidOperationException or NotSupportedException)
            { Log.Write("Mixer session became unavailable", ex); }
            finally { owned?.Dispose(); }
        }
        foreach (var id in _sessions.Keys.Where(id => !found.Contains(id)).ToArray()) ReleaseSession(id);
        return snapshots.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
    });

    private readonly Dictionary<string, System.Windows.Media.ImageSource> _appIcons = new();
    private System.Windows.Media.ImageSource? SessionIcon(AudioSessionControl session)
    {
        try
        {
            if (session.IsSystemSoundsSession) return null;
            using var process = Process.GetProcessById((int)session.GetProcessID);
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (_appIcons.TryGetValue(path, out var cached)) return cached;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(20, 20));
            bitmap.Freeze();
            _appIcons[path] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }
    private static string SessionName(AudioSessionControl session)
    {
        if (session.IsSystemSoundsSession) return "Sonidos del sistema";
        var name = session.DisplayName;
        if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('@')) return name;
        try
        {
            using var process = Process.GetProcessById((int)session.GetProcessID);
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            return string.IsNullOrWhiteSpace(description) ? process.ProcessName : description;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return "Aplicación · " + session.GetProcessID; }
    }
    private void SessionCreated(object? sender, AudioSessionControl session) => Notify();

    // Resolve by endpoint + instance ID at write time. A closed/replaced app must
    // never redirect a queued slider write to a different session or output.
    private void WithSession(string deviceId, string sessionId, Action<AudioSessionControl> write)
    {
        var manager = Get(deviceId).AudioSessionManager;
        manager.RefreshSessions();
        var collection = manager.Sessions;
        for (var i = 0; i < collection.Count; i++)
        {
            using var session = collection[i];
            if (session.GetSessionInstanceIdentifier != sessionId) continue;
            if (session.State == AudioSessionState.AudioSessionStateExpired) break;
            write(session); return;
        }
        throw new InvalidOperationException("La aplicación cerró su sesión de audio.");
    }
    public Task SetSessionVolumeAsync(string deviceId, string sessionId, int volume) => RunAsync(() =>
    { WithSession(deviceId, sessionId, s => s.SimpleAudioVolume.Volume = Math.Clamp(volume, 0, 100) / 100f); return true; });
    public Task SetSessionMuteAsync(string deviceId, string sessionId, bool muted) => RunAsync(() =>
    { WithSession(deviceId, sessionId, s => s.SimpleAudioVolume.Mute = muted); return true; });

    private void ReleaseSession(string id)
    {
        var session = _sessions[id];
        try { if (_sessionEvents is not null) session.UnRegisterEventClient(_sessionEvents); }
        catch (Exception ex) when (ex is COMException or CoreAudioException) { Log.Write("Mixer session cleanup", ex); }
        session.Dispose(); _sessions.Remove(id);
    }
    private void StopWatchingSessions()
    {
        if (_watchedManager is not null) _watchedManager.OnSessionCreated -= SessionCreated;
        foreach (var id in _sessions.Keys.ToArray()) ReleaseSession(id);
        _sessionEvents = null; _watchedEndpoint = null; _watchedManager = null;
    }
    public Task StopWatchingSessionsAsync() => RunAsync(() => { StopWatchingSessions(); return true; });

    public Task<SpatialState> GetSpatialStateAsync(string deviceId) => RunAsync(() =>
    {
        _ = Get(deviceId);
        var enabled = SpatialAudioStateReader.Read(deviceId);
        return new SpatialState(enabled, enabled.HasValue && SpatialAudioBackend.Available,
            !enabled.HasValue ? "Windows no informa sonido espacial para esta salida. Usá el botón de configuración."
            : !SpatialAudioBackend.Available ? "Falta el componente de sonido espacial. Usá el botón de configuración."
            : "Activar Windows Sonic / desactivar sonido espacial. El engranaje permite elegir Dolby o DTS en Windows.");
    });
    public async Task<SpatialState> SetSpatialEnabledAsync(string deviceId, bool enabled)
    {
        // Do not block the audio apartment while the vendor command talks to Windows.
        var before = await GetSpatialStateAsync(deviceId).ConfigureAwait(false);
        if (!before.CanToggle) throw new InvalidOperationException(before.Hint);
        await SpatialAudioBackend.SetAsync(deviceId, enabled).ConfigureAwait(false);
        SpatialState? result = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await Task.Delay(100).ConfigureAwait(false);
            result = await GetSpatialStateAsync(deviceId).ConfigureAwait(false);
            if (result.Enabled == enabled) return result;
        }
        throw new InvalidOperationException("Windows no aplicó el cambio espacial. Revisá compatibilidad y proveedores desde el engranaje.");
    }

    private sealed class SessionEvents(Action changed) : IAudioSessionEventsHandler
    {
        public void OnVolumeChanged(float volume, bool muted) => changed();
        public void OnDisplayNameChanged(string name) => changed();
        public void OnIconPathChanged(string path) => changed();
        public void OnChannelVolumeChanged(uint count, IntPtr volumes, uint changedChannel) => changed();
        public void OnGroupingParamChanged(ref Guid groupingId) => changed();
        public void OnStateChanged(AudioSessionState state) => changed();
        public void OnSessionDisconnected(AudioSessionDisconnectReason reason) => changed();
    }
}


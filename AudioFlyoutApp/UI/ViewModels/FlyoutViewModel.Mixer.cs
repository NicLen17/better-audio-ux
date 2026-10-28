using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using BetterAudio.Core.Audio;
using BetterAudio.Core.Models;

namespace BetterAudio.UI.ViewModels;

public sealed partial class FlyoutViewModel
{
    private IAudioMixerService? _mixer;
    private bool _isMixerOpen;
    private bool _mixerRefreshing;
    private bool _mixerRefreshAgain;
    private bool _mixerLoading;
    private int _mixerVersion;
    private DispatcherTimer? _sessionWriteTimer;
    private readonly Dictionary<string, (string Device, string Session, int Volume)> _pendingSessionVolumes = new();
    private string? _writingSession;
    private SpatialState _spatialState = new(null, false, "Consultando sonido espacial…");
    private bool _spatialBusy;
    private int _spatialVersion;
    private string? _spatialEndpoint;
    private DateTime _lastSpatialCheck;
    public ObservableCollection<MixerSessionModel> Sessions { get; } = new();
    public bool IsMixerOpen => _isMixerOpen;
    public bool HasSessions => Sessions.Count > 0;
    public string MixerEmptyText => _mixerLoading ? "Buscando aplicaciones…" : "Abrí una aplicación con audio en esta salida.";
    public bool? SpatialEnabled => _spatialState.Enabled;
    public bool CanToggleSpatial => HasDevices && _spatialState.CanToggle && !_spatialBusy;
    public string SpatialHint => _spatialBusy ? "Aplicando sonido espacial…" : _spatialState.Hint;

    private void InitializeMixer()
    {
        _mixer = _audio as IAudioMixerService;
        _sessionWriteTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(45) };
        _sessionWriteTimer.Tick += async (_, _) => { _sessionWriteTimer.Stop(); await FlushSessionWritesAsync(); };
    }
    public async Task OpenMixerAsync()
    {
        _isMixerOpen = true; _mixerVersion++; Message = "";
        Notify(nameof(IsMixerOpen)); Notify(nameof(HudHeight));
        await RefreshMixerAsync();
    }
    public async Task CloseMixerAsync()
    {
        _isMixerOpen = false; _mixerVersion++;
        Notify(nameof(IsMixerOpen)); Notify(nameof(HudHeight));
        if (_mixer is not null)
            try { await _mixer.StopWatchingSessionsAsync(); }
            catch (Exception ex) { if (!_disposed) ReportError("No se pudo cerrar el mezclador.", ex); }
    }
    public async Task RefreshMixerAsync()
    {
        if (!_isMixerOpen || _disposed) return;
        if (_mixerRefreshing) { _mixerRefreshAgain = true; return; }
        var device = SelectedDevice;
        if (device is null) { Sessions.Clear(); Notify(nameof(HasSessions)); return; }
        if (_mixer is null) { Message = "El mezclador no está disponible."; return; }
        _mixerRefreshing = true; _mixerLoading = true; Notify(nameof(MixerEmptyText));
        var version = _mixerVersion;
        try
        {
            var snapshots = await _mixer.GetSessionsAsync(device.Id);
            if (!_isMixerOpen || _disposed || version != _mixerVersion || SelectedDevice?.Id != device.Id) return;
            foreach (var old in Sessions.Where(s => !snapshots.Any(n => n.Id == s.Id && n.DeviceId == s.DeviceId && n.Name == s.Name)).ToArray()) Sessions.Remove(old);
            for (var i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                var item = Sessions.FirstOrDefault(s => s.Id == snapshot.Id && s.DeviceId == snapshot.DeviceId);
                if (item is null) { item = new MixerSessionModel(snapshot, QueueSessionVolume); Sessions.Insert(i, item); }
                else
                {
                    var key = SessionKey(item.DeviceId, item.Id);
                    item.Update(snapshot, _pendingSessionVolumes.ContainsKey(key) || _writingSession == key);
                    var from = Sessions.IndexOf(item); if (from != i) Sessions.Move(from, i);
                }
            }
            Notify(nameof(HasSessions));
        }
        catch (Exception ex) { if (_isMixerOpen && !_disposed) ReportError("No se pudieron leer las aplicaciones de esta salida.", ex); }
        finally
        {
            _mixerRefreshing = false; _mixerLoading = false; Notify(nameof(MixerEmptyText));
            if (_mixerRefreshAgain && !_disposed && _isMixerOpen) { _mixerRefreshAgain = false; await RefreshMixerAsync(); }
        }
    }
    private static string SessionKey(string device, string session) => device.Length + ":" + device + session;
    private void QueueSessionVolume(MixerSessionModel session, int volume)
    {
        _pendingSessionVolumes[SessionKey(session.DeviceId, session.Id)] = (session.DeviceId, session.Id, volume);
        _sessionWriteTimer!.Stop(); _sessionWriteTimer.Start();
    }
    private async Task FlushSessionWritesAsync()
    {
        if (_writingSession is not null || _mixer is null) return;
        while (_pendingSessionVolumes.Count > 0 && !_disposed)
        {
            var pending = _pendingSessionVolumes.First();
            _pendingSessionVolumes.Remove(pending.Key); _writingSession = pending.Key;
            try { await _mixer.SetSessionVolumeAsync(pending.Value.Device, pending.Value.Session, pending.Value.Volume); }
            catch (Exception ex) { ReportError("La aplicación ya no permite cambiar su volumen.", ex); }
            finally { _writingSession = null; }
        }
        await RefreshMixerAsync();
    }
    public async Task ToggleSessionMuteAsync(MixerSessionModel session)
    {
        if (_mixer is null) return;
        try { await _mixer.SetSessionMuteAsync(session.DeviceId, session.Id, !session.Muted); await RefreshMixerAsync(); }
        catch (Exception ex) { ReportError("No se pudo cambiar el silencio de esta aplicación.", ex); }
    }

    private void SetSpatialState(SpatialState state)
    {
        _spatialState = state;
        Notify(nameof(SpatialEnabled)); Notify(nameof(CanToggleSpatial)); Notify(nameof(SpatialHint));
    }
    public async Task RefreshSpatialAsync(bool force = false)
    {
        var id = SelectedDevice?.Id;
        if (_disposed || _spatialBusy) return;
        if (!force && _spatialEndpoint == id && DateTime.UtcNow - _lastSpatialCheck < TimeSpan.FromSeconds(1)) return;
        _spatialEndpoint = id; _lastSpatialCheck = DateTime.UtcNow;
        var version = ++_spatialVersion;
        if (id is null || _mixer is null) { SetSpatialState(new(null, false, "Usá el engranaje para configurar sonido espacial en Windows.")); return; }
        try
        {
            var state = await _mixer.GetSpatialStateAsync(id);
            if (!_disposed && version == _spatialVersion && SelectedDevice?.Id == id) SetSpatialState(state);
        }
        catch (Exception ex)
        {
            if (version == _spatialVersion && SelectedDevice?.Id == id)
                SetSpatialState(new(null, false, "No se pudo consultar el estado espacial. Usá el engranaje de Windows."));
            Services.Log.Write("Could not query spatial audio", ex);
        }
    }
    public async Task SetSpatialEnabledAsync(bool enabled)
    {
        var id = SelectedDevice?.Id;
        if (id is null || _mixer is null || !CanToggleSpatial) return;
        _spatialBusy = true; ++_spatialVersion;
        Notify(nameof(CanToggleSpatial)); Notify(nameof(SpatialHint)); Notify(nameof(SpatialEnabled));
        try
        {
            var actual = await _mixer.SetSpatialEnabledAsync(id, enabled);
            if (SelectedDevice?.Id == id) SetSpatialState(actual);
            Message = "";
        }
        catch (Exception ex) { ReportError("Windows no aplicó el cambio espacial. Revisá el engranaje.", ex); }
        finally
        {
            _spatialBusy = false;
            await RefreshSpatialAsync(force: true);
            Notify(nameof(CanToggleSpatial)); Notify(nameof(SpatialHint));
        }
    }
}

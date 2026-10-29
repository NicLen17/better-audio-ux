using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using BetterAudio.Core.Audio;
using BetterAudio.Core.Models;
using BetterAudio.Services;

namespace BetterAudio.UI.ViewModels;

public sealed partial class FlyoutViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAudioDeviceManager _audio;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _volumeTimer;
    private readonly IUserPreferences _preferences;
    private bool _autoSwitch;
    private List<string> _deviceOrder;
    private string? _pendingDefaultId;
    private Task _switchTask = Task.CompletedTask;
    private int _selectedIndex = -1;
    private int _volume;
    private bool _muted;
    private bool _refreshing;
    private bool _refreshAgain;
    private TaskCompletionSource? _refreshCompletion;
    private bool _writingVolume;
    private string? _writingVolumeId;
    private int _writingVolumeValue;
    private string? _displayedVolumeId;
    private bool _disposed;
    private bool _isBusy;
    private string _message = "";
    private readonly Dictionary<string, int> _pendingVolumes = new();
    public ObservableCollection<AudioDeviceModel> Devices { get; } = new();
    public AudioDeviceModel? SelectedDevice => _selectedIndex >= 0 && _selectedIndex < Devices.Count ? Devices[_selectedIndex] : null;
    public int SelectedIndex => _selectedIndex;
    public bool HasDevices => Devices.Count > 0;
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; Notify(); Notify(nameof(CanInteract)); } }
    public bool CanInteract => HasDevices;
    public string Counter => HasDevices ? $"{_selectedIndex + 1:00} / {Devices.Count:00}" : "00 / 00";
    public string DeviceCount => $"{Devices.Count} {(Devices.Count == 1 ? "dispositivo conectado" : "dispositivos conectados")}";
    public string SelectedName => SelectedDevice?.DisplayName ?? "Conectá una salida de audio";
    public bool SelectedIsDefault => SelectedDevice?.IsDefault == true;
    public bool AutoSwitchOnNavigation
    {
        get => _autoSwitch;
        set
        {
            if (_autoSwitch == value) return;
            _autoSwitch = value;
            Notify();
            if (!value) _pendingDefaultId = null;
            try { _preferences.AutoSwitchOnNavigation = value; }
            catch (Exception ex) { ReportError("La preferencia se aplicará solo durante esta sesión.", ex); }
        }
    }
    public string SelectionHint => SelectedDevice?.IsDefault == true ? "Salida actual del sistema" : "Enter para usar esta salida";
    public string VolumeGlyph => _muted || _volume == 0 ? "\uE74F" : "\uE767";
    public string MuteLabel => _muted ? "Activar sonido (M)" : "Silenciar (M)";
    public string Message { get => _message; private set { _message = value; Notify(); Notify(nameof(HasMessage)); Notify(nameof(HudHeight)); } }
    public double HudHeight => (IsMixerOpen || IsSettingsOpen || IsMirrorOpen ? 482 : 378) + (HasMessage ? 28 : 0);
    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public int Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (_volume == clamped || SelectedDevice is null) return;
            _volume = clamped;
            Notify(); Notify(nameof(VolumeGlyph));
            _pendingVolumes[SelectedDevice.Id] = clamped;
            _volumeTimer.Stop(); _volumeTimer.Start();
        }
    }

    public FlyoutViewModel(IAudioDeviceManager audio, Dispatcher dispatcher, IUserPreferences? preferences = null, IStartupService? startup = null)
    {
        _audio = audio; _dispatcher = dispatcher;
        _preferences = preferences ?? new UserPreferences();
        _autoSwitch = _preferences.AutoSwitchOnNavigation;
        _deviceOrder = _preferences.DeviceOrder.ToList();
        _hiddenDevices = new Dictionary<string, string>(_preferences.HiddenDevices);
        _startup = startup ?? new StartupService();
        UpdateHiddenDevices();
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        _volumeTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(45) };
        _refreshTimer.Tick += async (_, _) => { _refreshTimer.Stop(); await RefreshAsync(); };
        _volumeTimer.Tick += async (_, _) => { _volumeTimer.Stop(); await FlushVolumeAsync(); };
        _audio.Changed += AudioChanged;
        InitializeMixer();
    }

    private void AudioChanged(object? sender, EventArgs args)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(() => { if (!_disposed) { _refreshTimer.Stop(); _refreshTimer.Start(); } });
    }

    public async Task RefreshAsync(bool focusDefault = false)
    {
        if (_disposed) return;
        if (_refreshing)
        {
            _refreshAgain = true;
            var pending = _refreshCompletion?.Task;
            if (pending is not null) await pending;
            if (focusDefault && !_disposed) await RefreshAsync(focusDefault: true);
            return;
        }
        _refreshing = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _refreshCompletion = completion;
        try
        {
            var available = await _audio.GetDevicesAsync();
            var snapshots = available.Where(d => !_hiddenDevices.ContainsKey(d.Id)).OrderBy(d =>
            {
                var rank = _deviceOrder.IndexOf(d.Id);
                return rank < 0 ? int.MaxValue : rank;
            }).ToList();
            if (_disposed) return;
            var followDefault = focusDefault || (_autoSwitch && !IsBusy && _pendingDefaultId is null);
            var selectedId = followDefault ? null : SelectedDevice?.Id;
            foreach (var old in Devices.Where(d => !snapshots.Any(s => s.Id == d.Id)).ToArray()) Devices.Remove(old);
            for (var index = 0; index < snapshots.Count; index++)
            {
                var snapshot = snapshots[index];
                var existing = Devices.FirstOrDefault(d => d.Id == snapshot.Id);
                if (existing is not null && existing.Name != snapshot.Name) { Devices.Remove(existing); existing = null; }
                if (existing is null) { existing = new AudioDeviceModel(snapshot); Devices.Insert(index, existing); }
                else { existing.Update(snapshot); var from = Devices.IndexOf(existing); if (from != index) Devices.Move(from, index); }
            }
            var target = Devices.FirstOrDefault(d => d.Id == selectedId) ?? Devices.FirstOrDefault(d => d.IsDefault) ?? Devices.FirstOrDefault();
            SetSelection(target is null ? -1 : Devices.IndexOf(target));
            Notify(nameof(HasDevices)); Notify(nameof(CanTestSound)); Notify(nameof(CanInteract)); Notify(nameof(DeviceCount));
            await RefreshMirrorAsync(available);
            _ = RefreshSpatialAsync();
            if (IsMixerOpen) await RefreshMixerAsync();
        }
        catch (Exception ex) { ReportError("No se pudo leer el audio. Comprobá la conexión y volvé a intentar.", ex); }
        finally
        {
            _refreshing = false;
            _refreshCompletion = null;
            completion.TrySetResult();
            if (_refreshAgain && !_disposed) { _refreshAgain = false; _refreshTimer.Start(); }
        }
    }

    private void SetSelection(int index)
    {
        _selectedIndex = index;
        for (var i = 0; i < Devices.Count; i++) { Devices[i].IsPreview = i == index; Devices[i].Order = i + 1; }
        var selected = SelectedDevice;
        if (selected is null || _displayedVolumeId != selected.Id ||
            (_writingVolumeId != selected.Id && !_pendingVolumes.ContainsKey(selected.Id)))
        {
            _volume = selected is null ? 0 : _pendingVolumes.GetValueOrDefault(selected.Id,
                _writingVolumeId == selected.Id ? _writingVolumeValue : selected.Volume);
            _displayedVolumeId = selected?.Id;
            Notify(nameof(Volume));
        }
        _muted = SelectedDevice?.Muted ?? false;
        Notify(nameof(SelectedIndex)); Notify(nameof(SelectedDevice)); Notify(nameof(Counter));
        Notify(nameof(SelectedName)); Notify(nameof(SelectionHint)); Notify(nameof(VolumeGlyph)); Notify(nameof(MuteLabel));
        Notify(nameof(SelectedIsDefault));
    }

    public void MoveSelection(int delta)
    {
        if (!HasDevices) return;
        Message = "";
        SetSelection(((_selectedIndex + delta) % Devices.Count + Devices.Count) % Devices.Count);
        if (_autoSwitch && SelectedDevice is { } device) _ = RequestSwitchAsync(device.Id);
    }
    public void SetSelectedPosition(int position)
    {
        if (SelectedDevice is not { } selected || position < 1 || position > Devices.Count || position == _selectedIndex + 1) return;
        Devices.Move(_selectedIndex, position - 1);
        // Keep disconnected endpoints in their saved slots so reconnecting restores their place.
        var visible = new Queue<string>(Devices.Select(d => d.Id));
        var connected = Devices.Select(d => d.Id).ToHashSet();
        var order = _deviceOrder.Select(id => connected.Contains(id) ? visible.Dequeue() : id).ToList();
        order.AddRange(visible);
        _deviceOrder = order;
        SetSelection(Devices.IndexOf(selected));
        Message = "";
        try { _preferences.DeviceOrder = order; }
        catch (Exception ex) { ReportError("El orden se aplicará solo durante esta sesión.", ex); }
    }

    public void AdjustVolume(int delta) { if (CanInteract) Volume += delta; }

    public Task CommitAsync(AudioDeviceModel? device = null)
    {
        var target = device ?? SelectedDevice;
        if (target is null || !Devices.Contains(target)) return Task.CompletedTask;
        SetSelection(Devices.IndexOf(target));
        return RequestSwitchAsync(target.Id);
    }

    private Task RequestSwitchAsync(string id)
    {
        _pendingDefaultId = id;
        if (IsBusy) return _switchTask;
        _switchTask = ProcessSwitchesAsync();
        return _switchTask;
    }

    private async Task ProcessSwitchesAsync()
    {
        IsBusy = true;
        try
        {
            while (_pendingDefaultId is { } id && !_disposed)
            {
                _pendingDefaultId = null;
                if (!Devices.Any(d => d.Id == id)) continue;
                try { await _audio.SetDefaultAsync(id); Message = ""; }
                catch (Exception ex) { ReportError("No se pudo activar esta salida. Comprobá su conexión.", ex); }
                if (!_disposed) await RefreshAsync();
            }
        }
        finally { IsBusy = false; }
    }

    private bool _isTestingSound;
    public bool CanTestSound => HasDevices && !_isTestingSound;
    public string TestSoundLabel => _isTestingSound ? "Probando…" : "Probar";
    public async Task TestSelectedSoundAsync()
    {
        if (!CanTestSound || SelectedDevice is not { } device) return;
        if (_muted || _volume == 0)
        {
            Message = "Activá el sonido y subí el volumen de esta salida para probarla.";
            return;
        }
        _isTestingSound = true;
        Notify(nameof(CanTestSound)); Notify(nameof(TestSoundLabel));
        Message = "";
        try { await _audio.PlayTestSoundAsync(device.Id); }
        catch (Exception ex) { if (!_disposed) ReportError("No se pudo probar esta salida. Comprobá su conexión.", ex); }
        finally { _isTestingSound = false; Notify(nameof(CanTestSound)); Notify(nameof(TestSoundLabel)); }
    }

    public async Task ToggleMuteAsync()
    {
        var device = SelectedDevice;
        if (device is null) return;
        try { await _audio.SetMuteAsync(device.Id, !_muted); await RefreshAsync(); }
        catch (Exception ex) { ReportError("No se pudo cambiar el silencio.", ex); await RefreshAsync(); }
    }

    private async Task FlushVolumeAsync()
    {
        if (_writingVolume || _pendingVolumes.Count == 0 || _disposed) return;
        var pending = _pendingVolumes.First();
        _pendingVolumes.Remove(pending.Key);
        _writingVolume = true;
        _writingVolumeId = pending.Key;
        _writingVolumeValue = pending.Value;
        try { await _audio.SetVolumeAsync(pending.Key, pending.Value); }
        catch (Exception ex) { ReportError("No se pudo ajustar el volumen. Volvé a elegir una salida.", ex); }
        finally
        {
            _writingVolume = false;
            _writingVolumeId = null;
            if (_pendingVolumes.Count > 0) _volumeTimer.Start();
            else await RefreshAsync();
        }
    }

    private void ReportError(string text, Exception error) { Message = text; Log.Write(text, error); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose()
    {
        _disposed = true; _pendingDefaultId = null; _refreshTimer.Stop(); _volumeTimer.Stop(); _audio.Changed -= AudioChanged;
        _sessionWriteTimer?.Stop();
    }
}

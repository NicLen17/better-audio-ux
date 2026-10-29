using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BetterAudio.Core.Audio;
using BetterAudio.Core.Models;

namespace BetterAudio.UI.ViewModels;

public sealed partial class FlyoutViewModel
{
    private bool _isMirrorOpen;
    private bool _mirrorBusy;
    private AudioMirrorState _mirrorState = new();
    private string _mirrorRoute = "";
    private AudioDeviceModel? _mirrorSource;
    private AudioDeviceModel? _mirrorTarget;
    private int _mirrorCopyVolume = 70;
    public ObservableCollection<AudioDeviceModel> MirrorDevices { get; } = new();
    public bool IsMirrorOpen => _isMirrorOpen;
    public bool IsMirroring => _mirrorState.IsActive;
    public bool CanConfigureMirror => !_mirrorBusy && !IsMirroring;
    public bool CanToggleMirror => !_mirrorBusy && _audio is IAudioMirrorService &&
        (IsMirroring || (MirrorSource is not null && MirrorTarget is not null && MirrorSource.Id != MirrorTarget.Id));
    public string MirrorEntryLabel => IsMirroring ? "Duplicar ●" : "Duplicar";
    public string MirrorActionLabel => _mirrorBusy ? "Aplicando…" : IsMirroring ? "Detener duplicación" : "Activar duplicación";
    public string MirrorStatus => _mirrorState.Error ?? (IsMirroring
        ? _mirrorRoute
        : "La duplicación está desactivada.");
    public AudioDeviceModel? MirrorSource
    {
        get => _mirrorSource;
        set { if (!CanConfigureMirror) return; _mirrorSource = value; Notify(); Notify(nameof(CanToggleMirror)); }
    }
    public AudioDeviceModel? MirrorTarget
    {
        get => _mirrorTarget;
        set { if (!CanConfigureMirror) return; _mirrorTarget = value; Notify(); Notify(nameof(CanToggleMirror)); }
    }
    public int MirrorCopyVolume
    {
        get => _mirrorCopyVolume;
        set { _mirrorCopyVolume = Math.Clamp(value, 0, 100); Notify(); _ = ApplyMirrorVolumeAsync(); }
    }
    private async Task ApplyMirrorVolumeAsync()
    {
        if (_audio is not IAudioMirrorService service || _disposed) return;
        try { await service.SetMirrorVolumeAsync(_mirrorCopyVolume); }
        catch (Exception ex) { if (!_disposed) ReportError("No se pudo ajustar el volumen de la copia.", ex); }
    }
    public async Task OpenMirrorAsync()
    {
        CloseSettings();
        await CloseMixerAsync();
        _isMirrorOpen = true; Message = "";
        await RefreshAsync();
        Notify(nameof(IsMirrorOpen)); Notify(nameof(HudHeight));
    }
    public void CloseMirror()
    {
        _isMirrorOpen = false;
        Notify(nameof(IsMirrorOpen)); Notify(nameof(HudHeight));
    }
    private async Task RefreshMirrorAsync(System.Collections.Generic.IReadOnlyList<DeviceSnapshot> available)
    {
        if (_audio is not IAudioMirrorService service) return;
        var state = await service.GetMirrorStateAsync();
        if (_disposed) return;
        var sourceId = state.SourceId ?? _mirrorSource?.Id ?? SelectedDevice?.Id;
        var targetId = state.TargetId ?? _mirrorTarget?.Id;
        _mirrorRoute = $"Activo: {available.FirstOrDefault(d => d.Id == state.SourceId)?.Name} → {available.FirstOrDefault(d => d.Id == state.TargetId)?.Name}";
        var visible = available.Where(d => !_hiddenDevices.ContainsKey(d.Id)).ToArray();
        foreach (var old in MirrorDevices.Where(d => !visible.Any(s => s.Id == d.Id && s.Name == d.Name)).ToArray()) MirrorDevices.Remove(old);
        foreach (var snapshot in visible)
        {
            var existing = MirrorDevices.FirstOrDefault(d => d.Id == snapshot.Id);
            if (existing is null) MirrorDevices.Add(new AudioDeviceModel(snapshot));
            else existing.Update(snapshot);
        }
        _mirrorState = state;
        _mirrorSource = MirrorDevices.FirstOrDefault(d => d.Id == sourceId) ?? (state.IsActive ? null : MirrorDevices.FirstOrDefault(d => d.IsDefault) ?? MirrorDevices.FirstOrDefault());
        _mirrorTarget = MirrorDevices.FirstOrDefault(d => d.Id == targetId) ?? (state.IsActive ? null : MirrorDevices.FirstOrDefault(d => d.Id != _mirrorSource?.Id));
        NotifyMirror();
    }
    private void NotifyMirror()
    {
        Notify(nameof(MirrorSource)); Notify(nameof(MirrorTarget)); Notify(nameof(IsMirroring));
        Notify(nameof(CanConfigureMirror)); Notify(nameof(CanToggleMirror)); Notify(nameof(MirrorEntryLabel));
        Notify(nameof(MirrorActionLabel)); Notify(nameof(MirrorStatus));
    }
    public async Task ToggleMirrorAsync()
    {
        if (!CanToggleMirror || _audio is not IAudioMirrorService service) return;
        _mirrorBusy = true; NotifyMirror(); Message = "";
        try
        {
            if (IsMirroring) await service.StopMirrorAsync();
            else await service.StartMirrorAsync(MirrorSource!.Id, MirrorTarget!.Id, MirrorCopyVolume);
        }
        catch (Exception ex) { ReportError("No se pudo duplicar el audio. Comprobá las salidas y volvé a intentar.", ex); }
        finally { _mirrorBusy = false; await RefreshAsync(); NotifyMirror(); }
    }
}

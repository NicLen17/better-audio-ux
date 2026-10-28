using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BetterAudio.Core.Models;

public sealed record SessionSnapshot(string DeviceId, string Id, string Name, int Volume, bool Muted, bool Active, System.Windows.Media.ImageSource? Icon = null);
public sealed record SpatialState(bool? Enabled, bool CanToggle, string Hint);

public sealed class MixerSessionModel : INotifyPropertyChanged
{
    private readonly Action<MixerSessionModel, int> _volumeChanged;
    private int _volume;
    private bool _muted;
    public string DeviceId { get; }
    public string Id { get; }
    public string Name { get; }
    public System.Windows.Media.ImageSource? Icon { get; }
    public string MuteGlyph => _muted ? "\uE74F" : "\uE767";
    public string MuteLabel => _muted ? "Activar sonido de " + Name : "Silenciar " + Name;
    public bool Muted => _muted;
    public int Volume
    {
        get => _volume;
        set
        {
            var volume = Math.Clamp(value, 0, 100);
            if (_volume == volume) return;
            _volume = volume; Notify(); _volumeChanged(this, volume);
        }
    }
    public MixerSessionModel(SessionSnapshot snapshot, Action<MixerSessionModel, int> volumeChanged)
    {
        DeviceId = snapshot.DeviceId; Id = snapshot.Id; Name = snapshot.Name; Icon = snapshot.Icon;
        _volume = snapshot.Volume; _muted = snapshot.Muted; _volumeChanged = volumeChanged;
    }
    public void Update(SessionSnapshot snapshot, bool keepLocalVolume)
    {
        if (!keepLocalVolume) { _volume = snapshot.Volume; Notify(nameof(Volume)); }
        _muted = snapshot.Muted; Notify(nameof(MuteGlyph)); Notify(nameof(MuteLabel)); Notify(nameof(Muted));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}


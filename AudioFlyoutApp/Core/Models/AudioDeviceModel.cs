using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace BetterAudio.Core.Models;

public sealed record DeviceSnapshot(string Id, string Name, string Kind, string Glyph, int Volume, bool Muted, bool IsDefault);

public sealed class AudioDeviceModel : INotifyPropertyChanged
{
    public string Id { get; }
    public string Name { get; }
    public string DisplayName => GetDisplayName(Name);
    public string Kind { get; }
    public string Glyph { get; }
    public int Volume { get; private set; }
    public bool Muted { get; private set; }
    public bool IsDefault { get; private set; }
    public string Status => IsDefault ? "● EN USO" : "DISPONIBLE";
    private bool _isPreview;
    public bool IsPreview { get => _isPreview; set { _isPreview = value; OnPropertyChanged(); } }

    public AudioDeviceModel(DeviceSnapshot snapshot)
    {
        Id = snapshot.Id; Name = snapshot.Name; Kind = snapshot.Kind; Glyph = snapshot.Glyph;
        Volume = snapshot.Volume; Muted = snapshot.Muted; IsDefault = snapshot.IsDefault;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private static string GetDisplayName(string name)
    {
        var opening = name.IndexOf('(');
        if (opening > 0 && name.EndsWith(')'))
        {
            var prefix = name[..opening].Trim();
            name = Regex.IsMatch(prefix, "^(altavoces|speakers|headphones|headset|auriculares|audífono)", RegexOptions.IgnoreCase)
                ? name[(opening + 1)..^1] : prefix;
        }
        name = Regex.Replace(name, @"^\d+\s*-\s*", "");
        name = Regex.Replace(name, @"\s+(WIRELESS|Gaming Headset)\b", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, "CORSAIR", "Corsair", RegexOptions.IgnoreCase);
        return Regex.Replace(name, @"\s+", " ").Trim();
    }
    public void Update(DeviceSnapshot snapshot)
    {
        Volume = snapshot.Volume; Muted = snapshot.Muted; IsDefault = snapshot.IsDefault;
        OnPropertyChanged(nameof(Volume)); OnPropertyChanged(nameof(Muted));
        OnPropertyChanged(nameof(IsDefault)); OnPropertyChanged(nameof(Status));
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

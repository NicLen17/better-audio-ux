using System;
using System.IO;
using System.Text.Json;

namespace BetterAudio.Services;

public interface IUserPreferences
{
    bool AutoSwitchOnNavigation { get; set; }
}

public sealed class UserPreferences : IUserPreferences
{
    private sealed record Settings(bool AutoSwitchOnNavigation = true);
    private readonly string _path;
    private bool _autoSwitch = true;
    public UserPreferences(string? path = null)
    {
        _path = path ?? Path.Combine(Log.DirectoryPath, "settings.json");
        try
        {
            if (File.Exists(_path)) _autoSwitch = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path))?.AutoSwitchOnNavigation ?? true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Log.Write("Could not load preferences; using defaults", ex); }
    }
    public bool AutoSwitchOnNavigation
    {
        get => _autoSwitch;
        set
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(value)));
            File.Move(temporary, _path, true);
            _autoSwitch = value;
        }
    }
}

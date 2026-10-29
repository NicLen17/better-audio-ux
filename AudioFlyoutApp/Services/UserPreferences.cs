using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterAudio.Services;

public interface IUserPreferences
{
    bool AutoSwitchOnNavigation { get; set; }
    IReadOnlyList<string> DeviceOrder { get; set; }
    IReadOnlyDictionary<string, string> HiddenDevices { get; set; }
}

public sealed class UserPreferences : IUserPreferences
{
    private sealed record Settings(bool AutoSwitchOnNavigation = true, string[]? DeviceOrder = null, Dictionary<string, string>? HiddenDevices = null)
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalSettings { get; init; }
    }
    private static readonly object SaveGate = new();
    private readonly string _path;
    private Settings _settings;

    public UserPreferences(string? path = null)
    {
        // Stable per-user location, independent of executable/build directory.
        _path = Path.GetFullPath(path ?? Path.Combine(Log.DirectoryPath, "settings.json"));
        lock (SaveGate) _settings = LoadSettings();
    }
    private Settings? ReadSettings(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));
            return settings is null ? null : settings with
            {
                DeviceOrder = settings.DeviceOrder?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray()
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Log.Write("Could not read preferences: " + path, ex); return null; }
    }
    private Settings LoadSettings() => ReadSettings(_path) ?? ReadSettings(_path + ".bak") ?? new Settings();

    public bool AutoSwitchOnNavigation
    {
        get => _settings.AutoSwitchOnNavigation;
        set => Save(settings => settings with { AutoSwitchOnNavigation = value });
    }
    public IReadOnlyList<string> DeviceOrder
    {
        get => Array.AsReadOnly(_settings.DeviceOrder ?? []);
        set
        {
            var order = value.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
            Save(settings => settings with { DeviceOrder = order });
        }
    }
    public IReadOnlyDictionary<string, string> HiddenDevices
    {
        get => new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(_settings.HiddenDevices ?? new());
        set
        {
            var hidden = new Dictionary<string, string>(value);
            Save(settings => settings with { HiddenDevices = hidden });
        }
    }
    private void Save(Func<Settings, Settings> update)
    {
        lock (SaveGate)
        {
            // Merge only the changed preference into the latest saved document.
            var settings = update(LoadSettings());
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var data = JsonSerializer.SerializeToUtf8Bytes(settings);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(_path) && ReadSettings(_path) is not null)
                    File.Replace(temporary, _path, _path + ".bak");
                else
                    File.Move(temporary, _path, overwrite: true);
                // Keep a recovery copy even after the first change.
                if (!File.Exists(_path + ".bak")) File.Copy(_path, _path + ".bak");
                _settings = settings;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}

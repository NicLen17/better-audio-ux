using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BetterAudio.Core.Models;

namespace BetterAudio.Core.Audio;

public interface IAudioDeviceManager : IDisposable
{
    event EventHandler? Changed;
    Task<IReadOnlyList<DeviceSnapshot>> GetDevicesAsync();
    Task SetDefaultAsync(string id);
    Task SetVolumeAsync(string id, int volume);
    Task SetMuteAsync(string id, bool muted);
}

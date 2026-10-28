using System.Collections.Generic;
using System.Threading.Tasks;
using BetterAudio.Core.Models;

namespace BetterAudio.Core.Audio;

public interface IAudioMixerService
{
    Task<IReadOnlyList<SessionSnapshot>> GetSessionsAsync(string deviceId);
    Task SetSessionVolumeAsync(string deviceId, string sessionId, int volume);
    Task SetSessionMuteAsync(string deviceId, string sessionId, bool muted);
    Task StopWatchingSessionsAsync();
    Task<SpatialState> GetSpatialStateAsync(string deviceId);
    Task<SpatialState> SetSpatialEnabledAsync(string deviceId, bool enabled);
}

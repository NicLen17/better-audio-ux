using System.Threading.Tasks;

namespace BetterAudio.Core.Audio;

public sealed record AudioMirrorState(string? SourceId = null, string? TargetId = null, string? Error = null)
{
    public bool IsActive => SourceId is not null && TargetId is not null;
}

public interface IAudioMirrorService
{
    Task<AudioMirrorState> GetMirrorStateAsync();
    Task StartMirrorAsync(string sourceId, string targetId, int volume);
    Task StopMirrorAsync();
    Task SetMirrorVolumeAsync(int volume);
}

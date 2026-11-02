using System.Threading.Tasks;

namespace BetterAudio.Core.Audio;

public sealed record InputListenState(string? InputId = null, string? OutputId = null, string? OutputName = null, string? Error = null)
{
    public bool IsActive => InputId is not null && OutputId is not null;
}

public interface IAudioInputService
{
    Task<InputListenState> GetListenStateAsync();
    Task StartListeningAsync(string inputId);
    Task StopListeningAsync();
}

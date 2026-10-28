using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BetterAudio.Core.Audio;

internal static class SpatialAudioBackend
{
    internal static string Executable => Path.Combine(AppContext.BaseDirectory, "ThirdParty", "SoundVolumeCommandLine", "svcl.exe");
    public static bool Available => File.Exists(Executable);
    public static async Task SetAsync(string id, bool enabled)
    {
        if (!Available) throw new FileNotFoundException("El componente de sonido espacial no está disponible.");
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(Executable)!
        };
        start.ArgumentList.Add("/SetSpatial");
        start.ArgumentList.Add(id);
        start.ArgumentList.Add(enabled ? "{b53d940c-b846-4831-9f76-d102b9b725a0}" : "");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar el componente espacial.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new TimeoutException("Windows tardó demasiado en configurar sonido espacial."); }
        await output.ConfigureAwait(false);
        var details = await error.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException("Windows rechazó el cambio de sonido espacial. " + details);
        // A successful process exit alone is not evidence that a provider was enabled.
        // The caller must re-read ISpatialAudioClient and verify the resulting state.
    }
}

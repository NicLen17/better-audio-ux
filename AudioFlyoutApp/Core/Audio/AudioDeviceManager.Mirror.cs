using System;
using System.Threading.Tasks;
using BetterAudio.Services;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BetterAudio.Core.Audio;

public sealed partial class AudioDeviceManager : IAudioMirrorService
{
    private WasapiRecorder? _mirrorCapture;
    private WasapiPlayer? _mirrorOutput;
    private VolumeSampleProvider? _mirrorVolume;
    private AudioMirrorState _mirrorState = new();

    public Task<AudioMirrorState> GetMirrorStateAsync() => RunAsync(() => _mirrorState);
    public Task StartMirrorAsync(string sourceId, string targetId, int volume) => RunAsync(() =>
    {
        if (sourceId == targetId) throw new InvalidOperationException("Elegí dos salidas diferentes.");
        var source = Get(sourceId);
        var target = Get(targetId);
        StopMirror();
        try
        {
            // Capture native channels: the automatic stereo loopback conversion loses level.
            var capture = new WasapiRecorderBuilder().WithDevice(source).WithLoopbackCapture()
                .WithBufferLength(20).Build();
            _mirrorCapture = capture;
            var buffer = new BufferedWaveProvider(capture.WaveFormat, TimeSpan.FromMilliseconds(400))
            {
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
            _mirrorVolume = new VolumeSampleProvider(new MirrorStereoSampleProvider(buffer.ToSampleProvider(),
                capture.WaveFormat is WaveFormatExtensible extensible ? (uint)extensible.ChannelMask : 0)) { Volume = Math.Clamp(volume, 0, 100) / 100f };
            var output = new WasapiPlayerBuilder().WithDevice(target).WithSharedMode().WithEventSync().WithLatency(50).Build();
            _mirrorOutput = output;
            capture.DataAvailable += (data, flags, position, timestamp) =>
            {
                // Bound accumulated delay when device clocks drift or a driver stalls.
                if (buffer.BufferedDuration > TimeSpan.FromMilliseconds(150)) buffer.ClearBuffer();
                buffer.AddSamples(data);
            };
            capture.RecordingStopped += (_, args) => QueueMirrorStopped(capture, args.Exception);
            output.PlaybackStopped += (_, args) => QueueMirrorStopped(capture, args.Exception);
            output.Init(_mirrorVolume.ToWaveProvider());
            _mirrorState = new(sourceId, targetId);
            capture.StartRecording();
            output.Play();
            Notify();
            return true;
        }
        catch
        {
            StopMirror();
            throw;
        }
    });

    private void QueueMirrorStopped(WasapiRecorder capture, Exception? error)
    {
        if (_disposed) return;
        // Never dispose a capture/player from its own audio callback thread.
        _ = RunAsync(() =>
        {
            if (!ReferenceEquals(capture, _mirrorCapture)) return false;
            if (error is not null) Log.Write("Audio mirror stopped", error);
            StopMirror("La duplicación se detuvo. Comprobá las salidas y volvé a activarla.");
            return true;
        });
    }

    public Task StopMirrorAsync() => RunAsync(() => { StopMirror(); return true; });
    public Task SetMirrorVolumeAsync(int volume) => RunAsync(() =>
    {
        if (_mirrorVolume is not null) _mirrorVolume.Volume = Math.Clamp(volume, 0, 100) / 100f;
        return true;
    });

    private void StopMirror(string? error = null)
    {
        var capture = _mirrorCapture;
        var output = _mirrorOutput;
        _mirrorCapture = null;
        _mirrorOutput = null;
        _mirrorVolume = null;
        _mirrorState = new(Error: error);
        try { capture?.Dispose(); }
        finally
        {
            try { output?.Dispose(); }
            finally { Notify(); }
        }
    }
}



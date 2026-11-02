using System;
using System.Threading.Tasks;
using BetterAudio.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BetterAudio.Core.Audio;

public sealed partial class AudioDeviceManager : IAudioInputService
{
    private WasapiRecorder? _inputCapture;
    private WasapiPlayer? _inputPlayback;
    private MMDevice? _listenOutput;
    private InputListenState _listenState = new();

    public Task<InputListenState> GetListenStateAsync() => RunAsync(() => _listenState);
    public Task StartListeningAsync(string inputId) => RunAsync(() =>
    {
        var input = Get(inputId);
        if (input.DataFlow != DataFlow.Capture) throw new InvalidOperationException("Elegí una entrada de audio.");
        StopListening();
        try
        {
            if (_enumerator is null || !_enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var output))
                throw new InvalidOperationException("Conectá una salida para escuchar el micrófono.");
            _listenOutput = output;
            if (output.AudioEndpointVolume.Mute || output.AudioEndpointVolume.MasterVolumeLevelScalar == 0)
                throw new InvalidOperationException("Activá el sonido de la salida actual para escuchar la entrada.");
            var capture = new WasapiRecorderBuilder().WithDevice(input).WithBufferLength(20)
                .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)).Build();
            _inputCapture = capture;
            var buffer = new BufferedWaveProvider(capture.WaveFormat, TimeSpan.FromMilliseconds(300))
            { DiscardOnBufferOverflow = true, ReadFully = true };
            var player = new WasapiPlayerBuilder().WithDevice(output).WithSharedMode().WithEventSync().WithLatency(50).Build();
            _inputPlayback = player;
            capture.DataAvailable += (data, flags, position, timestamp) =>
            {
                if (buffer.BufferedDuration > TimeSpan.FromMilliseconds(150)) buffer.ClearBuffer();
                buffer.AddSamples(data);
            };
            capture.RecordingStopped += (_, args) => QueueListenStopped(capture, args.Exception);
            player.PlaybackStopped += (_, args) => QueueListenStopped(capture, args.Exception);
            // A moderate preview gain avoids a sudden full-volume microphone monitor.
            player.Init(new VolumeSampleProvider(buffer.ToSampleProvider()) { Volume = .5f }.ToWaveProvider());
            _listenState = new(inputId, output.ID, output.FriendlyName);
            capture.StartRecording();
            player.Play();
            Notify();
            return true;
        }
        catch { StopListening(); throw; }
    });

    private void QueueListenStopped(WasapiRecorder capture, Exception? error)
    {
        if (_disposed) return;
        _ = RunAsync(() =>
        {
            if (!ReferenceEquals(capture, _inputCapture)) return false;
            if (error is not null) Log.Write("Microphone listening stopped", error);
            StopListening("La escucha se detuvo. Revisá la conexión y los permisos del micrófono.");
            return true;
        });
    }

    public Task StopListeningAsync() => RunAsync(() => { StopListening(); return true; });
    private void StopListening(string? error = null)
    {
        var capture = _inputCapture;
        var playback = _inputPlayback;
        var output = _listenOutput;
        _inputCapture = null; _inputPlayback = null; _listenOutput = null;
        _listenState = new(Error: error);
        try { capture?.Dispose(); }
        finally
        {
            try { playback?.Dispose(); }
            finally { output?.Dispose(); Notify(); }
        }
    }
}

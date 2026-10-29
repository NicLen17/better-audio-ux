using System;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BetterAudio.Core.Audio;

public sealed partial class AudioDeviceManager
{
    private WasapiPlayer? _testOutput;
    private string? _testDeviceId;
    private TaskCompletionSource? _testCompletion;

    public async Task PlayTestSoundAsync(string id)
    {
        var completion = await RunAsync(() =>
        {
            StopTestSound();
            var device = Get(id);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _testCompletion = done;
            _testDeviceId = id;
            _testOutput = new WasapiPlayerBuilder().WithDevice(device).WithSharedMode().WithEventSync().WithLatency(100).Build();
            _testOutput.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is { } error) done.TrySetException(error);
                else done.TrySetResult();
            };
            try { _testOutput.Init(new TestToneProvider()); _testOutput.Play(); }
            catch { StopTestSound(); throw; }
            return done;
        }).ConfigureAwait(false);
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        finally
        {
            if (!_disposed)
                await RunAsync(() =>
                {
                    if (ReferenceEquals(_testCompletion, completion)) StopTestSound();
                    return true;
                }).ConfigureAwait(false);
        }
    }
    private void StopTestSound()
    {
        var output = _testOutput;
        _testOutput = null;
        _testDeviceId = null;
        var completion = _testCompletion;
        _testCompletion = null;
        try { output?.Dispose(); }
        finally { completion?.TrySetCanceled(); }
    }
}

// A short two-note chime, with fades to avoid clicks and conservative amplitude.
internal sealed class TestToneProvider : IWaveProvider
{
    public WaveFormat WaveFormat { get; } = new WaveFormat(48000, 16, 2);
    private int _frame;
    public int Read(byte[] buffer, int offset, int count)
    => Read(buffer.AsSpan(offset, count));
    public int Read(Span<byte> buffer)
    {
        var frames = Math.Min(buffer.Length / 4, 33600 - _frame);
        for (var i = 0; i < frames; i++, _frame++)
        {
            var time = _frame / 48000.0;
            var noteTime = time < .3 ? time : time - .35;
            var frequency = time < .3 ? 523.25 : 659.25;
            var envelope = Math.Clamp(Math.Min(noteTime / .015, (.28 - noteTime) / .06), 0, 1);
            var sample = (short)(Math.Sin(2 * Math.PI * frequency * Math.Max(0, noteTime)) * envelope * .12 * short.MaxValue);
            var index = i * 4;
            buffer[index] = buffer[index + 2] = (byte)sample;
            buffer[index + 1] = buffer[index + 3] = (byte)(sample >> 8);
        }
        return frames * 4;
    }
}

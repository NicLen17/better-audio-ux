using System;
using System.Numerics;
using NAudio.Wave;

namespace BetterAudio.Core.Audio;

// Keep front L/R at unity when folding a surround endpoint into a stereo copy.
// Windows' automatic 7.1 -> stereo matrix reserves headroom by attenuating even
// stereo-only content. Limit only frames that would actually exceed full scale.
internal sealed class MirrorStereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly (float Left, float Right)[] _weights;
    private float[] _buffer = [];
    public WaveFormat WaveFormat { get; }
    public MirrorStereoSampleProvider(ISampleProvider source, uint channelMask)
    {
        _source = source;
        var channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        if (BitOperations.PopCount(channelMask) != channels)
            channelMask = channels switch { 1 => 4, 2 => 3, 4 => 0x33, 6 => 0x3f, 8 => 0x63f, _ => throw new NotSupportedException("Distribución de canales no compatible.") };
        _weights = new (float, float)[channels];
        var channel = 0;
        for (var bit = 0; bit < 32; bit++)
        {
            if ((channelMask & (1u << bit)) == 0) continue;
            _weights[channel++] = (1u << bit) switch
            {
                1 => (1, 0), 2 => (0, 1), 4 => channels == 1 ? (1, 1) : (.7071f, .7071f),
                8 => (.5f, .5f), 16 or 64 or 512 => (.7071f, 0),
                32 or 128 or 1024 => (0, .7071f), _ => (.5f, .5f)
            };
        }
    }
    public int Read(Span<float> output)
    {
        var channels = _source.WaveFormat.Channels;
        var requested = output.Length / 2 * channels;
        if (_buffer.Length < requested) _buffer = new float[requested];
        var read = _source.Read(_buffer.AsSpan(0, requested));
        var frames = read / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            float left = 0, right = 0;
            for (var c = 0; c < channels; c++)
            {
                var sample = _buffer[frame * channels + c];
                left += sample * _weights[c].Left; right += sample * _weights[c].Right;
            }
            var divisor = Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right)));
            output[frame * 2] = left / divisor; output[frame * 2 + 1] = right / divisor;
        }
        return frames * 2;
    }
}

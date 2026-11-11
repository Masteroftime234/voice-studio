using NAudio.Wave;

namespace VoiceStudio.Audio.Processing;

/// <summary>
/// Ramène n'importe quelle source (mono, stéréo, multicanal) en stéréo.
/// Mono : dupliqué sur les deux canaux. Multicanal : on garde gauche/droite avant.
/// </summary>
public sealed class StereoAdapterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private float[] _scratch = new float[4096];

    public StereoAdapterSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_sourceChannels == 2) return _source.Read(buffer, offset, count);

        int frames = count / 2;
        int needed = frames * _sourceChannels;
        if (_scratch.Length < needed) _scratch = new float[needed]; // exceptionnel : seulement si le bloc grandit

        int framesRead = _source.Read(_scratch, 0, needed) / _sourceChannels;
        if (_sourceChannels == 1)
        {
            for (int i = 0, o = offset; i < framesRead; i++)
            {
                float sample = _scratch[i];
                buffer[o++] = sample;
                buffer[o++] = sample;
            }
        }
        else
        {
            for (int i = 0, s = 0, o = offset; i < framesRead; i++, s += _sourceChannels)
            {
                buffer[o++] = _scratch[s];
                buffer[o++] = _scratch[s + 1];
            }
        }
        return framesRead * 2;
    }
}

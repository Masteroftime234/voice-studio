using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceStudio.Audio.Processing;
using VoiceStudio.Audio.Routing;

namespace VoiceStudio.Audio.Capture;

/// <summary>
/// Relie un thread de capture au mixeur :
/// octets du périphérique → float → tampon circulaire → stéréo → 48 kHz.
/// <see cref="Write"/> est appelé par le thread de capture, <see cref="Output"/> est lu par le thread de sortie.
/// </summary>
public sealed class CaptureSourcePipeline
{
    private readonly AudioRingBuffer _ring;
    private readonly RingBufferSampleProvider _ringProvider;
    private float[] _convertBuffer;

    public CaptureSourcePipeline(int sampleRate, int channels)
    {
        var nativeFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _ring = new AudioRingBuffer(sampleRate * channels); // 1 seconde
        _ringProvider = new RingBufferSampleProvider(_ring, nativeFormat);
        _convertBuffer = new float[sampleRate * channels / 4];

        ISampleProvider chain = new StereoAdapterSampleProvider(_ringProvider);
        if (sampleRate != AudioConstants.SampleRate)
            chain = new WdlResamplingSampleProvider(chain, AudioConstants.SampleRate);
        Output = chain;
    }

    /// <summary>Sortie 48 kHz stéréo, à brancher sur une tranche du mixeur.</summary>
    public ISampleProvider Output { get; }

    public long Underruns => _ringProvider.Underruns;
    public long Overruns => _ringProvider.Overruns;

    /// <summary>Appelé par le thread de capture avec les octets bruts reçus.</summary>
    public void Write(ReadOnlySpan<byte> data, SampleKind kind)
    {
        int samples = data.Length / SampleConverter.BytesPerSample(kind);
        if (samples == 0) return;
        if (_convertBuffer.Length < samples) _convertBuffer = new float[samples]; // exceptionnel

        var floats = _convertBuffer.AsSpan(0, samples);
        SampleConverter.ToFloat(data, kind, floats);
        _ring.Write(floats); // si le tampon est plein, l'excédent est abandonné
    }
}

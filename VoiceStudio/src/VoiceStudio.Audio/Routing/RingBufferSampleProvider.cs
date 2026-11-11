using NAudio.Wave;

namespace VoiceStudio.Audio.Routing;

/// <summary>
/// Expose un <see cref="AudioRingBuffer"/> comme source NAudio.
///
/// Les horloges de la carte son d'entrée et de la sortie ne sont jamais parfaitement d'accord,
/// et un à-coup du système peut retarder la capture. Cette classe absorbe ces écarts :
/// - elle garde un petit coussin de données avant de commencer à jouer ;
/// - quand les données manquent, elle complète en silence avec une courte descente,
///   puis remonte en douceur au retour du son : un manque s'entend comme un blanc, pas un craquement ;
/// - quand le retard s'accumule (dérive d'horloge), elle le rattrape.
/// </summary>
public sealed class RingBufferSampleProvider : ISampleProvider
{
    private readonly AudioRingBuffer _ring;
    private readonly int _prefillSamples;
    private readonly int _maxSamples;
    private readonly int _fadeSamples;
    private bool _primed;
    private bool _fadeInPending = true;
    private long _underruns;
    private long _overruns;

    public RingBufferSampleProvider(AudioRingBuffer ring, WaveFormat format,
        int prefillMilliseconds = 25, int maxMilliseconds = 150)
    {
        _ring = ring;
        WaveFormat = format;
        int samplesPerMs = format.SampleRate * format.Channels / 1000;
        _prefillSamples = Align(samplesPerMs * prefillMilliseconds, format.Channels);
        _maxSamples = Align(samplesPerMs * maxMilliseconds, format.Channels);
        _fadeSamples = Math.Max(format.Channels, Align(samplesPerMs * 2, format.Channels)); // 2 ms
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>Nombre de fois où la source n'a pas fourni assez de données.</summary>
    public long Underruns => Interlocked.Read(ref _underruns);

    /// <summary>Nombre de rattrapages de latence effectués.</summary>
    public long Overruns => Interlocked.Read(ref _overruns);

    public int Read(float[] buffer, int offset, int count)
    {
        var destination = buffer.AsSpan(offset, count);
        int available = _ring.Available;

        if (available > _maxSamples)
        {
            _ring.Skip(Align(available - _prefillSamples, WaveFormat.Channels));
            Interlocked.Increment(ref _overruns);
            _fadeInPending = true;
        }

        if (!_primed)
        {
            if (_ring.Available < Math.Max(_prefillSamples, count))
            {
                destination.Clear();
                return count;
            }
            _primed = true;
            _fadeInPending = true;
        }

        int read = _ring.Read(destination);

        if (read < count)
        {
            // Pas assez de données : on descend à zéro au lieu de couper net.
            if (read > 0) FadeOut(destination.Slice(0, read));
            else _primed = false; // tampon vide : on attend un nouveau coussin
            destination.Slice(read).Clear();
            _fadeInPending = true;
            Interlocked.Increment(ref _underruns);
            return count;
        }

        if (_fadeInPending)
        {
            FadeIn(destination);
            _fadeInPending = false;
        }
        return count;
    }

    private void FadeIn(Span<float> samples)
    {
        int length = Math.Min(_fadeSamples, samples.Length);
        for (int i = 0; i < length; i++) samples[i] *= i / (float)length;
    }

    private void FadeOut(Span<float> samples)
    {
        int length = Math.Min(_fadeSamples, samples.Length);
        int start = samples.Length - length;
        for (int i = 0; i < length; i++) samples[start + i] *= 1f - i / (float)length;
    }

    private static int Align(int samples, int channels) => samples - (samples % channels);
}

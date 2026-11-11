using NAudio.Wave;

namespace VoiceStudio.Audio.Routing;

/// <summary>
/// Expose un <see cref="AudioRingBuffer"/> comme source NAudio.
/// - Retourne toujours le nombre d'échantillons demandé (silence si la source est en retard).
/// - Après un manque de données, attend un petit pré-remplissage avant de reprendre
///   (évite les craquements en rafale).
/// - Si le tampon grossit trop (dérive d'horloge entre périphériques), rattrape le retard
///   pour que la latence ne s'accumule jamais.
/// </summary>
public sealed class RingBufferSampleProvider : ISampleProvider
{
    private readonly AudioRingBuffer _ring;
    private readonly int _prefillSamples;
    private readonly int _maxSamples;
    private bool _primed;
    private long _underruns;
    private long _overruns;

    public RingBufferSampleProvider(AudioRingBuffer ring, WaveFormat format,
        int prefillMilliseconds = 20, int maxMilliseconds = 150)
    {
        _ring = ring;
        WaveFormat = format;
        int samplesPerMs = format.SampleRate * format.Channels / 1000;
        _prefillSamples = Align(samplesPerMs * prefillMilliseconds, format.Channels);
        _maxSamples = Align(samplesPerMs * maxMilliseconds, format.Channels);
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
            available = _ring.Available;
        }

        if (!_primed)
        {
            if (available < Math.Max(_prefillSamples, count))
            {
                destination.Clear();
                return count;
            }
            _primed = true;
        }

        int read = _ring.Read(destination);
        if (read < count)
        {
            destination.Slice(read).Clear();
            _primed = false;
            Interlocked.Increment(ref _underruns);
        }
        return count;
    }

    private static int Align(int samples, int channels) => samples - (samples % channels);
}

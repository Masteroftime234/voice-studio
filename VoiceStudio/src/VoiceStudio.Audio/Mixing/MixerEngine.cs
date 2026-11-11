using NAudio.Wave;
using VoiceStudio.Audio.Processing;

namespace VoiceStudio.Audio.Mixing;

/// <summary>
/// Cœur du mixage : additionne les tranches, applique le volume général et le limiteur.
/// C'est cette source que le périphérique de sortie (micro virtuel) vient lire.
/// </summary>
public sealed class MixerEngine : ISampleProvider
{
    private readonly MixerChannel[] _channels;
    private readonly SoftLimiter _limiter = new(AudioConstants.SampleRate);
    private readonly float _peakDecayPerFrame;
    private float[] _scratch = new float[AudioConstants.SampleRate / 5 * AudioConstants.Channels];
    private float _masterVolume = 1f;
    private float _currentMasterGain = 1f;
    private float _peak;
    private long _framesRendered;

    private MixerChannel? _duckKey;
    private MixerChannel[] _duckTargets = [];

    public MixerEngine(params MixerChannel[] channels)
    {
        _channels = channels;
        _peakDecayPerFrame = MathF.Pow(0.02f, 1f / AudioConstants.SampleRate);
    }

    public WaveFormat WaveFormat => AudioConstants.MixFormat;

    public IReadOnlyList<MixerChannel> Channels => _channels;

    public float MasterVolume
    {
        get => Volatile.Read(ref _masterVolume);
        set => Volatile.Write(ref _masterVolume, Math.Clamp(value, 0f, 2f));
    }

    /// <summary>Auto-ducking (inactif tant que <see cref="ConfigureDucking"/> n'a pas été appelé).</summary>
    public Ducker Ducker { get; } = new();

    /// <summary>
    /// Déclare quelle tranche déclenche la baisse (la voix) et lesquelles sont baissées.
    /// La tranche « voix » doit être placée avant elles dans la liste.
    /// </summary>
    public void ConfigureDucking(MixerChannel key, params MixerChannel[] targets)
    {
        foreach (var target in targets)
            if (Array.IndexOf(_channels, key) >= Array.IndexOf(_channels, target))
                throw new ArgumentException("La tranche de voix doit précéder les tranches à baisser.");
        _duckKey = key;
        _duckTargets = targets;
    }

    /// <summary>Niveau crête de la sortie finale, linéaire.</summary>
    public float Peak => Volatile.Read(ref _peak);

    /// <summary>Réduction appliquée par le limiteur (1 = aucune).</summary>
    public float LimiterGain => _limiter.CurrentGain;

    /// <summary>Compteur de trames envoyées à la sortie : s'il avance, le flux fonctionne.</summary>
    public long FramesRendered => Interlocked.Read(ref _framesRendered);

    public int Read(float[] buffer, int offset, int count)
    {
        if (_scratch.Length < count) _scratch = new float[count]; // exceptionnel : bloc plus grand que prévu

        Array.Clear(buffer, offset, count);
        int frames = count / 2;
        for (int c = 0; c < _channels.Length; c++)
        {
            float blockPeak = _channels[c].MixInto(buffer, offset, count, _scratch);
            if (_duckTargets.Length == 0 || _channels[c] != _duckKey) continue;

            float duckGain = Ducker.Update(blockPeak, frames, AudioConstants.SampleRate);
            for (int t = 0; t < _duckTargets.Length; t++) _duckTargets[t].ExternalGain = duckGain;
        }

        float target = MasterVolume;
        float gain = _currentMasterGain;
        float step = frames > 0 ? (target - gain) / frames : 0f;
        for (int i = offset; i < offset + count - 1; i += 2)
        {
            gain += step;
            buffer[i] *= gain;
            buffer[i + 1] *= gain;
        }
        _currentMasterGain = target;

        _limiter.Process(buffer, offset, count);

        float peak = _peak;
        for (int i = offset; i < offset + count - 1; i += 2)
        {
            float level = MathF.Max(MathF.Abs(buffer[i]), MathF.Abs(buffer[i + 1]));
            peak = level > peak ? level : peak * _peakDecayPerFrame;
        }
        Volatile.Write(ref _peak, peak);

        Interlocked.Add(ref _framesRendered, frames);
        return count;
    }
}

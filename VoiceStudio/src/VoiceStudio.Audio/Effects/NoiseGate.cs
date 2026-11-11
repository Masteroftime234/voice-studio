namespace VoiceStudio.Audio.Effects;

/// <summary>
/// Noise gate : coupe le micro quand on ne parle pas (bruit de fond, ventilateur, clavier lointain).
/// S'ouvre vite, se referme en douceur après un court maintien pour ne pas hacher les fins de mots.
/// </summary>
public sealed class NoiseGate
{
    private readonly float _envelopeRelease, _attack, _release;
    private readonly int _holdSamples;
    private float _thresholdDb = -45f;
    private bool _enabled;
    private float _envelope, _gain = 1f;
    private int _hold;
    private bool _isOpen = true;

    public NoiseGate(int sampleRate)
    {
        _envelopeRelease = MathF.Exp(-1f / (sampleRate * 0.010f));
        _attack = 1f - MathF.Exp(-1f / (sampleRate * 0.002f));
        _release = 1f - MathF.Exp(-1f / (sampleRate * 0.080f));
        _holdSamples = (int)(sampleRate * 0.150f);
    }

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled);
        set => Volatile.Write(ref _enabled, value);
    }

    /// <summary>Seuil d'ouverture en dB (‑70 = très sensible, ‑20 = il faut parler fort).</summary>
    public float ThresholdDb
    {
        get => Volatile.Read(ref _thresholdDb);
        set => Volatile.Write(ref _thresholdDb, Math.Clamp(value, -70f, -10f));
    }

    public bool IsOpen => Volatile.Read(ref _isOpen);

    public void Process(Span<float> samples)
    {
        float threshold = MathF.Pow(10f, ThresholdDb / 20f);
        float envelope = _envelope, gain = _gain;
        int hold = _hold;

        for (int i = 0; i < samples.Length; i++)
        {
            float level = MathF.Abs(samples[i]);
            envelope = level > envelope ? level : envelope * _envelopeRelease;

            if (envelope > threshold) hold = _holdSamples;
            else if (hold > 0) hold--;

            float target = hold > 0 ? 1f : 0f;
            gain += (target - gain) * (target > gain ? _attack : _release);
            samples[i] *= gain;
        }

        _envelope = envelope < 1e-20f ? 0f : envelope;
        _gain = gain;
        _hold = hold;
        Volatile.Write(ref _isOpen, hold > 0);
    }

    public void Reset()
    {
        _envelope = 0f;
        _gain = 1f;
        _hold = 0;
        Volatile.Write(ref _isOpen, true);
    }
}

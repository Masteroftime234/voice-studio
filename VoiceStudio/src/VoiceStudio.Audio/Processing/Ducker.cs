namespace VoiceStudio.Audio.Processing;

/// <summary>
/// Auto-ducking : baisse le volume des sons du PC quand on parle, puis le remonte progressivement.
/// Appelé une fois par bloc audio avec le niveau de la voix.
/// </summary>
public sealed class Ducker
{
    private bool _enabled;
    private float _thresholdDb = -35f;
    private float _minimumVolume = 0.25f;
    private float _attackMs = 80f;
    private float _releaseMs = 600f;
    private float _gain = 1f;
    private float _holdMs;

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled);
        set => Volatile.Write(ref _enabled, value);
    }

    /// <summary>Sensibilité : niveau de voix (dB) à partir duquel la musique baisse.</summary>
    public float ThresholdDb
    {
        get => Volatile.Read(ref _thresholdDb);
        set => Volatile.Write(ref _thresholdDb, Math.Clamp(value, -60f, -10f));
    }

    /// <summary>Volume minimum de la musique pendant qu'on parle (0 à 1).</summary>
    public float MinimumVolume
    {
        get => Volatile.Read(ref _minimumVolume);
        set => Volatile.Write(ref _minimumVolume, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>Vitesse de diminution, en millisecondes.</summary>
    public float AttackMs
    {
        get => Volatile.Read(ref _attackMs);
        set => Volatile.Write(ref _attackMs, Math.Clamp(value, 10f, 2000f));
    }

    /// <summary>Vitesse de remontée, en millisecondes.</summary>
    public float ReleaseMs
    {
        get => Volatile.Read(ref _releaseMs);
        set => Volatile.Write(ref _releaseMs, Math.Clamp(value, 50f, 5000f));
    }

    /// <summary>Gain appliqué en ce moment (1 = pas de baisse).</summary>
    public float CurrentGain => Volatile.Read(ref _gain);

    /// <summary>Met à jour le gain à partir du niveau crête de la voix sur ce bloc.</summary>
    public float Update(float voicePeak, int frames, int sampleRate)
    {
        const float holdDurationMs = 350f; // évite le « pompage » entre deux mots
        float blockMs = frames * 1000f / sampleRate;
        float gain = _gain;
        float target = 1f;

        if (Enabled)
        {
            if (voicePeak > MathF.Pow(10f, ThresholdDb / 20f)) _holdMs = holdDurationMs;
            else if (_holdMs > 0f) _holdMs -= blockMs;
            if (_holdMs > 0f) target = MinimumVolume;
        }
        else
        {
            _holdMs = 0f;
        }

        float time = target < gain ? AttackMs : ReleaseMs;
        gain += (target - gain) * (1f - MathF.Exp(-blockMs / time));
        if (MathF.Abs(target - gain) < 0.0005f) gain = target;

        Volatile.Write(ref _gain, gain);
        return gain;
    }
}

namespace VoiceStudio.Audio.Effects;

/// <summary>
/// Niveau automatique : amène la voix à un niveau confortable et l'y maintient,
/// que tu parles fort ou doucement, que tu sois près ou loin du micro.
///
/// Le gain ne bouge que lentement, et jamais pendant les silences : sans cela,
/// le bruit de fond serait amplifié dès que tu arrêtes de parler.
/// </summary>
public sealed class AutoLevel
{
    private readonly float _attack;
    private readonly float _release;
    private readonly float _envelopeAttack;
    private readonly float _envelopeRelease;
    private bool _enabled;
    private float _targetDb = -20f;
    private float _maximumGainDb = 18f;
    private float _envelope;
    private float _gain = 1f;

    public AutoLevel(int sampleRate)
    {
        _envelopeAttack = 1f - MathF.Exp(-1f / (sampleRate * 0.010f));
        _envelopeRelease = 1f - MathF.Exp(-1f / (sampleRate * 0.300f));
        _attack = 1f - MathF.Exp(-1f / (sampleRate * 0.400f));
        _release = 1f - MathF.Exp(-1f / (sampleRate * 1.500f));
    }

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled);
        set
        {
            if (Volatile.Read(ref _enabled) == value) return;
            Volatile.Write(ref _enabled, value);
            if (!value) Reset();
        }
    }

    /// <summary>Niveau visé, en dBFS.</summary>
    public float TargetDb
    {
        get => Volatile.Read(ref _targetDb);
        set => Volatile.Write(ref _targetDb, Math.Clamp(value, -40f, -6f));
    }

    /// <summary>Amplification maximale autorisée, pour ne pas faire monter le souffle.</summary>
    public float MaximumGainDb
    {
        get => Volatile.Read(ref _maximumGainDb);
        set => Volatile.Write(ref _maximumGainDb, Math.Clamp(value, 0f, 30f));
    }

    /// <summary>Gain appliqué en ce moment, en décibels.</summary>
    public float CurrentGainDb => 20f * MathF.Log10(MathF.Max(1e-6f, Volatile.Read(ref _gain)));

    public void Process(Span<float> samples)
    {
        float target = MathF.Pow(10f, TargetDb / 20f);
        float maximum = MathF.Pow(10f, MaximumGainDb / 20f);
        float silence = target * 0.06f; // en dessous, on considère que personne ne parle

        for (int i = 0; i < samples.Length; i++)
        {
            float level = MathF.Abs(samples[i]);
            _envelope += (level - _envelope) * (level > _envelope ? _envelopeAttack : _envelopeRelease);

            if (_envelope > silence)
            {
                float wanted = Math.Clamp(target / _envelope, 0.1f, maximum);
                _gain += (wanted - _gain) * (wanted < _gain ? _attack : _release);
            }

            samples[i] *= _gain;
        }

        if (MathF.Abs(_envelope) < 1e-20f) _envelope = 0f;
    }

    public void Reset()
    {
        _envelope = 0f;
        _gain = 1f;
    }
}

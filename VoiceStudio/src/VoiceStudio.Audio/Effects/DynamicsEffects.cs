namespace VoiceStudio.Audio.Effects;

/// <summary>
/// Compresseur : rapproche les passages forts des passages faibles, pour une voix
/// de niveau constant même quand on s'éloigne du micro ou qu'on hausse le ton.
/// </summary>
public sealed class CompressorEffect : VoiceEffect
{
    private readonly int _sampleRate;
    private readonly EffectParameter _threshold, _ratio, _attack, _release, _makeUp;
    private float _envelope;
    private float _gain = 1f;

    public CompressorEffect(int sampleRate) : base("compressor", "Compresseur",
        new EffectParameter("threshold", "Seuil", -40f, 0f, -18f, "dB"),
        new EffectParameter("ratio", "Taux", 1f, 10f, 3f, ": 1"),
        new EffectParameter("attack", "Attaque", 1f, 100f, 8f, "ms"),
        new EffectParameter("release", "Relâchement", 20f, 800f, 180f, "ms"),
        new EffectParameter("makeup", "Rattrapage", 0f, 18f, 6f, "dB"))
    {
        _sampleRate = sampleRate;
        _threshold = Parameters[0];
        _ratio = Parameters[1];
        _attack = Parameters[2];
        _release = Parameters[3];
        _makeUp = Parameters[4];
    }

    protected override void Process(Span<float> samples)
    {
        float threshold = MathF.Pow(10f, _threshold.Value / 20f);
        float ratio = MathF.Max(1f, _ratio.Value);
        float attack = Coefficient(_attack.Value);
        float release = Coefficient(_release.Value);
        float makeUp = MathF.Pow(10f, _makeUp.Value / 20f);

        for (int i = 0; i < samples.Length; i++)
        {
            float level = MathF.Abs(samples[i]);
            _envelope = level > _envelope
                ? _envelope + (level - _envelope) * attack
                : _envelope + (level - _envelope) * release;

            // Au-dessus du seuil, seule une fraction du dépassement est conservée.
            float target = 1f;
            if (_envelope > threshold && _envelope > 1e-9f)
            {
                float over = _envelope / threshold;
                target = MathF.Pow(over, 1f / ratio - 1f);
            }

            _gain += (target - _gain) * (target < _gain ? attack : release);
            samples[i] = Math.Clamp(samples[i] * _gain * makeUp, -1.5f, 1.5f);
        }

        if (MathF.Abs(_envelope) < 1e-20f) _envelope = 0f;
    }

    private float Coefficient(float milliseconds) =>
        1f - MathF.Exp(-1f / (_sampleRate * MathF.Max(0.1f, milliseconds) / 1000f));

    public override void Reset()
    {
        _envelope = 0f;
        _gain = 1f;
    }
}

/// <summary>
/// Dé-esseur : atténue les « s » et « ch » sifflants, sans toucher au reste de la voix.
/// Il isole la bande des sifflantes, mesure son niveau, et ne baisse que celle-là.
/// </summary>
public sealed class DeEsserEffect : VoiceEffect
{
    private readonly int _sampleRate;
    private readonly EffectParameter _frequency, _amount;
    private BiQuad _detector, _splitLow, _splitHigh;
    private float _lastFrequency = -1f;
    private float _envelope;
    private float[] _high = new float[4096];

    public DeEsserEffect(int sampleRate) : base("deesser", "Dé-esseur",
        new EffectParameter("frequency", "Début des sifflantes", 3000f, 10000f, 6000f, "Hz"),
        new EffectParameter("amount", "Intensité", 0f, 100f, 60f, "%"))
    {
        _sampleRate = sampleRate;
        _frequency = Parameters[0];
        _amount = Parameters[1];
    }

    protected override void Process(Span<float> samples)
    {
        float frequency = _frequency.Value;
        if (frequency != _lastFrequency)
        {
            _detector.SetHighPass(_sampleRate, frequency, 0.7f);
            _splitHigh.SetHighPass(_sampleRate, frequency, 0.7f);
            _splitLow.SetLowPass(_sampleRate, frequency, 0.7f);
            _lastFrequency = frequency;
        }

        if (_high.Length < samples.Length) _high = new float[samples.Length]; // exceptionnel
        float amount = _amount.Value / 100f;
        float attack = 1f - MathF.Exp(-1f / (_sampleRate * 0.002f));
        float release = 1f - MathF.Exp(-1f / (_sampleRate * 0.060f));

        for (int i = 0; i < samples.Length; i++)
        {
            float sibilance = MathF.Abs(_detector.Process(samples[i]));
            _envelope += (sibilance - _envelope) * (sibilance > _envelope ? attack : release);

            float low = _splitLow.Process(samples[i]);
            float high = _splitHigh.Process(samples[i]);

            // Plus la bande siffle, plus elle est réduite — la voix, elle, ne bouge pas.
            float reduction = 1f / (1f + _envelope * 40f * amount);
            samples[i] = low + high * reduction;
        }

        if (MathF.Abs(_envelope) < 1e-20f) _envelope = 0f;
    }

    public override void Reset()
    {
        _detector.Reset();
        _splitLow.Reset();
        _splitHigh.Reset();
        _envelope = 0f;
    }
}

/// <summary>
/// Anti-pop : enlève les coups de souffle sur les « p » et les « b », ainsi que les
/// vibrations du bureau, en coupant les fréquences trop basses pour porter la voix.
/// </summary>
public sealed class PopFilterEffect : VoiceEffect
{
    private readonly int _sampleRate;
    private readonly EffectParameter _frequency;
    private BiQuad _first, _second;
    private float _lastFrequency = -1f;

    public PopFilterEffect(int sampleRate) : base("popfilter", "Anti-pop",
        new EffectParameter("frequency", "Coupure des graves", 40f, 200f, 95f, "Hz"))
    {
        _sampleRate = sampleRate;
        _frequency = Parameters[0];
    }

    protected override void Process(Span<float> samples)
    {
        float frequency = _frequency.Value;
        if (frequency != _lastFrequency)
        {
            _first.SetHighPass(_sampleRate, frequency, 0.71f);
            _second.SetHighPass(_sampleRate, frequency, 0.71f);
            _lastFrequency = frequency;
        }

        for (int i = 0; i < samples.Length; i++)
            samples[i] = _second.Process(_first.Process(samples[i]));
    }

    public override void Reset()
    {
        _first.Reset();
        _second.Reset();
    }
}

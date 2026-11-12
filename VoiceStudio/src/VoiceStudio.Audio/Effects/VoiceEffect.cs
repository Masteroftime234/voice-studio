namespace VoiceStudio.Audio.Effects;

/// <summary>Un réglage d'effet, modifiable depuis l'interface pendant que l'audio tourne.</summary>
public sealed class EffectParameter
{
    private float _value;

    public EffectParameter(string id, string name, float min, float max, float defaultValue, string unit = "")
    {
        Id = id;
        Name = name;
        Min = min;
        Max = max;
        Default = defaultValue;
        Unit = unit;
        _value = defaultValue;
    }

    public string Id { get; }
    public string Name { get; }
    public float Min { get; }
    public float Max { get; }
    public float Default { get; }
    public string Unit { get; }

    public float Value
    {
        get => Volatile.Read(ref _value);
        set => Volatile.Write(ref _value, Math.Clamp(value, Min, Max));
    }
}

/// <summary>
/// Base des effets vocaux. Le traitement se fait en mono, 48 kHz, sur place, sur le thread audio :
/// aucune allocation ni verrou dans <see cref="Process"/>.
/// </summary>
public abstract class VoiceEffect
{
    private bool _enabled;

    private float[] _dry = new float[4096];

    protected VoiceEffect(string id, string name, params EffectParameter[] parameters)
    {
        Id = id;
        Name = name;
        Mix = new EffectParameter("mix_global", "Dosage", 0f, 100f, 100f, "%");
        Parameters = [.. parameters, Mix];
    }

    /// <summary>
    /// Proportion du son traité dans le résultat : 100 % = seulement l'effet,
    /// 50 % = moitié voix d'origine. Commun à tous les effets.
    /// </summary>
    public EffectParameter Mix { get; }

    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<EffectParameter> Parameters { get; }

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled);
        set => Volatile.Write(ref _enabled, value);
    }

    public EffectParameter? FindParameter(string id)
    {
        foreach (var parameter in Parameters)
            if (parameter.Id == id) return parameter;
        return null;
    }

    /// <summary>Applique l'effet puis le dose avec la voix d'origine.</summary>
    public void Apply(Span<float> samples)
    {
        float mix = Mix.Value / 100f;
        if (mix >= 0.999f)
        {
            Process(samples);
            return;
        }

        if (_dry.Length < samples.Length) _dry = new float[samples.Length]; // exceptionnel
        var dry = _dry.AsSpan(0, samples.Length);
        samples.CopyTo(dry);

        Process(samples);
        for (int i = 0; i < samples.Length; i++)
            samples[i] = dry[i] * (1f - mix) + samples[i] * mix;
    }

    protected abstract void Process(Span<float> samples);

    /// <summary>Vide la mémoire interne (lignes à retard, filtres) quand l'effet est réactivé.</summary>
    public virtual void Reset()
    {
    }
}

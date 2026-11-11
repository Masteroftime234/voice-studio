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

    protected VoiceEffect(string id, string name, params EffectParameter[] parameters)
    {
        Id = id;
        Name = name;
        Parameters = parameters;
    }

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

    public abstract void Process(Span<float> samples);

    /// <summary>Vide la mémoire interne (lignes à retard, filtres) quand l'effet est réactivé.</summary>
    public virtual void Reset()
    {
    }
}

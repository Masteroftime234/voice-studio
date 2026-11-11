namespace VoiceStudio.Audio.Effects;

/// <summary>État d'un effet dans un preset : activé ou non, et ses réglages.</summary>
public sealed class EffectState
{
    public bool Enabled { get; set; }
    public Dictionary<string, float> Parameters { get; set; } = new();
}

/// <summary>Un preset vocal : l'état de chaque effet. Sérialisable en JSON.</summary>
public sealed class VoicePreset
{
    public string Name { get; set; } = "";
    public bool IsBuiltIn { get; set; }
    public Dictionary<string, EffectState> Effects { get; set; } = new();

    public override string ToString() => Name;

    private static VoicePreset Create(string name, params (string Effect, (string Parameter, float Value)[] Values)[] effects)
    {
        var preset = new VoicePreset { Name = name, IsBuiltIn = true };
        foreach (var (effect, values) in effects)
            preset.Effects[effect] = new EffectState
            {
                Enabled = true,
                Parameters = values.ToDictionary(v => v.Parameter, v => v.Value)
            };
        return preset;
    }

    /// <summary>Presets fournis avec l'application.</summary>
    public static List<VoicePreset> CreateBuiltIns() =>
    [
        Create("Normal"),
        Create("Voix grave", ("pitch", [("semitones", -5f)])),
        Create("Voix aiguë", ("pitch", [("semitones", 6f)])),
        Create("Robot", ("robot", [("frequency", 60f), ("mix", 1f)])),
        Create("Radio", ("radio", [("drive", 4f)])),
        Create("Téléphone", ("telephone", [])),
        Create("Écho", ("echo", [("delay", 280f), ("feedback", 0.4f), ("mix", 0.45f)])),
        Create("Cathédrale", ("reverb", [("room", 0.9f), ("mix", 0.55f)])),
        Create("Démon",
            ("pitch", [("semitones", -8f)]),
            ("distortion", [("drive", 6f), ("level", 0.7f)]),
            ("reverb", [("room", 0.7f), ("mix", 0.3f)])),
        Create("Mégaphone",
            ("filter", [("mode", 1f), ("cutoff", 700f), ("resonance", 1.2f)]),
            ("distortion", [("drive", 18f), ("level", 0.5f)]))
    ];
}

namespace VoiceStudio.Settings;

/// <summary>
/// Un profil : un jeu de réglages complet qu'on rappelle d'un clic
/// (volumes, nettoyage du micro, effets, baisse automatique).
/// </summary>
public sealed class Profile
{
    public string Name { get; set; } = "";

    /// <summary>Profil fourni avec l'application : on peut l'appliquer, pas le supprimer.</summary>
    public bool IsBuiltIn { get; set; }

    public double MicrophoneVolume { get; set; } = 100;
    public double MasterVolume { get; set; } = 100;
    public double SoundboardVolume { get; set; } = 100;
    public double MicrophoneGainDb { get; set; }

    /// <summary>Volume de chaque bus, par identifiant (jeu, chat, media, autres).</summary>
    public Dictionary<string, double> BusVolumes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool NoiseSuppressionEnabled { get; set; }
    public double NoiseSuppressionStrength { get; set; } = 70;
    public bool NoiseGateEnabled { get; set; }
    public double NoiseGateThresholdDb { get; set; } = -45;

    public bool DuckingEnabled { get; set; }
    public double DuckingMinimumVolume { get; set; } = 25;

    public bool EffectsEnabled { get; set; } = true;

    /// <summary>Preset vocal appliqué, ou null pour ne pas y toucher.</summary>
    public string? PresetName { get; set; }

    public override string ToString() => Name;

    /// <summary>Les quatre profils fournis avec l'application.</summary>
    public static List<Profile> CreateBuiltIns() =>
    [
        new Profile
        {
            Name = "Discord",
            IsBuiltIn = true,
            // La voix passe devant, la musique reste en fond et recule quand tu parles.
            BusVolumes = Levels(55, 55, 55, 55),
            NoiseSuppressionEnabled = true,
            NoiseSuppressionStrength = 70,
            DuckingEnabled = true,
            DuckingMinimumVolume = 25,
            PresetName = "Normal"
        },
        new Profile
        {
            Name = "Gaming",
            IsBuiltIn = true,
            // Le jeu domine, le reste s'efface.
            BusVolumes = Levels(90, 70, 45, 60),
            NoiseSuppressionEnabled = true,
            NoiseSuppressionStrength = 60,
            DuckingEnabled = true,
            DuckingMinimumVolume = 40,
            PresetName = "Normal"
        },
        new Profile
        {
            Name = "Musique",
            IsBuiltIn = true,
            // On fait écouter un morceau : rien ne doit le hacher.
            MicrophoneVolume = 85,
            BusVolumes = Levels(60, 60, 100, 70),
            NoiseSuppressionEnabled = false,
            DuckingEnabled = false,
            PresetName = "Normal"
        },
        new Profile
        {
            Name = "Streaming",
            IsBuiltIn = true,
            // Voix propre et constante, fond sonore présent mais discret.
            BusVolumes = Levels(70, 70, 70, 70),
            NoiseSuppressionEnabled = true,
            NoiseSuppressionStrength = 60,
            NoiseGateEnabled = true,
            NoiseGateThresholdDb = -45,
            DuckingEnabled = true,
            DuckingMinimumVolume = 35,
            PresetName = "Normal"
        }
    ];

    private static Dictionary<string, double> Levels(double jeu, double chat, double media, double autres) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["jeu"] = jeu,
            ["chat"] = chat,
            ["media"] = media,
            ["autres"] = autres
        };
}

namespace VoiceStudio.Settings;

/// <summary>Réglages enregistrés entre deux lancements.</summary>
public sealed class AppSettings
{
    public string? MicrophoneId { get; set; }
    public string? OutputId { get; set; }

    /// <summary>Volumes en pourcentage (100 = niveau d'origine).</summary>
    public double MicrophoneVolume { get; set; } = 100;
    public double SystemAudioVolume { get; set; } = 60;
    public double MasterVolume { get; set; } = 100;

    public bool MicrophoneMuted { get; set; }
    public bool SystemAudioMuted { get; set; }

    /// <summary>Latence demandée pour la sortie virtuelle, en millisecondes.</summary>
    public int OutputLatencyMs { get; set; } = 40;

    /// <summary>Applications dont le son n'est jamais envoyé dans le micro virtuel.</summary>
    public List<string> ExcludedApps { get; set; } =
        ["Discord", "DiscordPTB", "DiscordCanary", "DiscordDevelopment"];
}

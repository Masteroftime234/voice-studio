namespace VoiceStudio.Settings;

/// <summary>Volume enregistré pour une application du mixeur.</summary>
public sealed class AppLevel
{
    public double Volume { get; set; } = 100;
    public bool Muted { get; set; }
}

/// <summary>Un son du soundboard.</summary>
public sealed class SoundEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public double Volume { get; set; } = 100;

    /// <summary>Code de touche Windows (0 = aucun raccourci).</summary>
    public int HotkeyVirtualKey { get; set; }

    public string HotkeyName { get; set; } = "";
}

/// <summary>Réglages enregistrés entre deux lancements.</summary>
public sealed class AppSettings
{
    // --- Périphériques ---
    public string? MicrophoneId { get; set; }
    public string? OutputId { get; set; }
    public string? MonitorId { get; set; }

    // --- Volumes (en pourcentage, 100 = niveau d'origine) ---
    public double MicrophoneVolume { get; set; } = 100;
    public double SoundboardVolume { get; set; } = 100;
    public double MasterVolume { get; set; } = 100;
    public double MonitorVolume { get; set; } = 70;
    public bool MicrophoneMuted { get; set; }
    public bool SoundboardMuted { get; set; }
    public bool MonitorEnabled { get; set; }

    /// <summary>Volume de chaque application, retenu par nom de processus.</summary>
    public Dictionary<string, AppLevel> AppLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bus de chaque application (jeu, chat, media, autres), retenu par nom de processus.</summary>
    public Dictionary<string, string> AppBuses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Volume et état « coupé » de chaque bus.</summary>
    public Dictionary<string, AppLevel> BusLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // --- Micro ---

    /// <summary>Gain appliqué au micro, en décibels (0 = niveau d'origine).</summary>
    public double MicrophoneGainDb { get; set; }

    /// <summary>
    /// Désactivée par défaut : sans effet ni nettoyage, le micro part vers Discord
    /// exactement tel qu'il est capté.
    /// </summary>
    public bool NoiseSuppressionEnabled { get; set; }
    public double NoiseSuppressionStrength { get; set; } = 70;
    public bool NoiseGateEnabled { get; set; }
    public double NoiseGateThresholdDb { get; set; } = -45;
    public bool PushToTalkEnabled { get; set; }
    public int PushToTalkVirtualKey { get; set; }
    public string PushToTalkName { get; set; } = "";

    /// <summary>L'avertissement sur les micros virtuels a été masqué par l'utilisateur.</summary>
    public bool MicrophoneWarningDismissed { get; set; }

    // --- Bascule micro / source ---
    public bool SwitchEnabled { get; set; }
    public string SwitchTargetId { get; set; } = "media";
    public int SwitchVirtualKey { get; set; }
    public string SwitchKeyName { get; set; } = "";

    /// <summary>Vrai : la touche se maintient. Faux : elle bascule à chaque appui.</summary>
    public bool SwitchHold { get; set; } = true;

    // --- Auto-ducking ---
    public bool DuckingEnabled { get; set; }
    public double DuckingThresholdDb { get; set; } = -35;
    public double DuckingMinimumVolume { get; set; } = 25;
    public double DuckingAttackMs { get; set; } = 80;
    public double DuckingReleaseMs { get; set; } = 600;

    // --- Effets vocaux ---
    public bool EffectsEnabled { get; set; } = true;
    public string? SelectedPreset { get; set; }
    public List<VoiceStudio.Audio.Effects.VoicePreset> CustomPresets { get; set; } = [];

    // --- Soundboard ---
    public List<SoundEntry> Sounds { get; set; } = [];
    public int MaxSimultaneousSounds { get; set; } = 4;

    // --- Audio ---
    public int OutputLatencyMs { get; set; } = 40;

    public List<string> ExcludedApps { get; set; } =
        ["Discord", "DiscordPTB", "DiscordCanary", "DiscordDevelopment"];
}

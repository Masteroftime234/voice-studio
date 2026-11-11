namespace VoiceStudio.Audio.Mixing;

/// <summary>
/// Un bus de mixage : un groupe d'applications réglé ensemble (Jeu, Chat, Média…).
/// Chaque application capturée est rangée dans un bus, et chaque bus a son volume,
/// son bouton couper et son indicateur de niveau.
/// </summary>
public sealed class AudioBus
{
    internal AudioBus(string id, string name, int sampleRate)
    {
        Id = id;
        Name = name;
        Channel = new MixerChannel(name, sampleRate);
        Channel.Source = Mix;
    }

    /// <summary>Identifiant stable, utilisé dans les réglages enregistrés.</summary>
    public string Id { get; }

    public string Name { get; }

    public MixerChannel Channel { get; }

    /// <summary>Somme des applications rangées dans ce bus.</summary>
    private MultiSourceSampleProvider Mix { get; } = new();

    /// <summary>Remplace les applications de ce bus (appelé hors du thread audio).</summary>
    internal void SetSources(AppAudioSource[] sources) => Mix.SetSources(sources);
}

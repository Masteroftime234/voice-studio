using VoiceStudio.Audio.Mixing;

namespace VoiceStudio.Audio.Services;

/// <summary>
/// Table de mixage de l'application : le micro, les bus d'applications, le soundboard,
/// puis le volume général.
/// </summary>
public sealed class AudioMixerService
{
    public AudioMixerService()
    {
        int rate = AudioConstants.SampleRate;
        Microphone = new MixerChannel("Micro", rate);
        Soundboard = new MixerChannel("Soundboard", rate);
        Buses =
        [
            new AudioBus("jeu", "Jeu", rate),
            new AudioBus("chat", "Chat", rate),
            new AudioBus("media", "Média", rate),
            new AudioBus("autres", "Autres", rate)
        ];

        var channels = new List<MixerChannel> { Microphone };
        channels.AddRange(Buses.Select(bus => bus.Channel));
        channels.Add(Soundboard);
        Engine = new MixerEngine(channels.ToArray());

        // Quand on parle, seuls les bus d'applications reculent : pas le soundboard.
        Engine.ConfigureDucking(Microphone, Buses.Select(bus => bus.Channel).ToArray());
        Engine.ConfigureSoundboardDucking(Soundboard);
    }

    public MixerChannel Microphone { get; }
    public MixerChannel Soundboard { get; }

    /// <summary>Les bus d'applications, dans l'ordre d'affichage.</summary>
    public IReadOnlyList<AudioBus> Buses { get; }

    public MixerEngine Engine { get; }

    public AudioBus? FindBus(string id) =>
        Buses.FirstOrDefault(bus => string.Equals(bus.Id, id, StringComparison.OrdinalIgnoreCase));
}

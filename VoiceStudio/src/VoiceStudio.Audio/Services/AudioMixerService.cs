using VoiceStudio.Audio.Mixing;

namespace VoiceStudio.Audio.Services;

/// <summary>Table de mixage de l'application : une tranche par source + le volume général.</summary>
public sealed class AudioMixerService
{
    public AudioMixerService()
    {
        Microphone = new MixerChannel("Microphone", AudioConstants.SampleRate);
        SystemAudio = new MixerChannel("Sons du PC", AudioConstants.SampleRate);
        Soundboard = new MixerChannel("Soundboard", AudioConstants.SampleRate);
        Engine = new MixerEngine(Microphone, SystemAudio, Soundboard);
        Engine.ConfigureDucking(Microphone, SystemAudio);
    }

    public MixerChannel Microphone { get; }
    public MixerChannel SystemAudio { get; }
    public MixerChannel Soundboard { get; }
    public MixerEngine Engine { get; }
}

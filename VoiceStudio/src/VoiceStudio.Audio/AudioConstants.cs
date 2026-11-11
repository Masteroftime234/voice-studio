using NAudio.Wave;

namespace VoiceStudio.Audio;

/// <summary>Format interne du moteur : tout est mixé en 48 kHz, stéréo, float 32 bits.</summary>
public static class AudioConstants
{
    public const int SampleRate = 48000;
    public const int Channels = 2;

    public static readonly WaveFormat MixFormat =
        WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
}

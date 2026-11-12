using NAudio.Wave;

namespace VoiceStudio.Audio.Mixing;

/// <summary>
/// Lecteur du soundboard : joue plusieurs sons déjà décodés en mémoire, en même temps.
/// Les sons sont déposés dans des emplacements par l'interface et consommés par le thread audio,
/// sans verrou.
/// </summary>
public sealed class SoundboardMixer : ISampleProvider
{
    public const int MaxVoices = 16;
    private const int FadeFrames = 240; // 5 ms : arrêt sans clic

    private sealed class Voice(string soundId, float[] data, PlaybackOptions options)
    {
        public readonly string SoundId = soundId;
        public readonly float[] Data = data;
        public readonly PlaybackOptions Options = options;
        public int Position = options.StartSample;
        public int FadeRemaining = -1;       // thread audio uniquement
        public volatile bool StopRequested;
    }

    private readonly Voice?[] _voices = new Voice?[MaxVoices];
    private int _maxSimultaneous = 4;

    public WaveFormat WaveFormat => AudioConstants.MixFormat;

    /// <summary>Un son est en cours de lecture (sert à baisser les autres sources).</summary>
    public bool IsAnyPlaying => ActiveCount > 0;

    /// <summary>Nombre maximum de sons joués en même temps (1 à 16).</summary>
    public int MaxSimultaneous
    {
        get => Volatile.Read(ref _maxSimultaneous);
        set => Volatile.Write(ref _maxSimultaneous, Math.Clamp(value, 1, MaxVoices));
    }

    public int ActiveCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _voices.Length; i++)
                if (Volatile.Read(ref _voices[i]) != null) count++;
            return count;
        }
    }

    public bool IsPlaying(string soundId)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            var voice = Volatile.Read(ref _voices[i]);
            if (voice != null && !voice.StopRequested && voice.SoundId == soundId) return true;
        }
        return false;
    }

    /// <summary>
    /// Lance un son (échantillons 48 kHz stéréo entrelacés).
    /// Retourne false si la limite de sons simultanés est atteinte.
    /// </summary>
    public bool Play(string soundId, float[] data, float volume) =>
        Play(soundId, data, new PlaybackOptions { Volume = volume });

    /// <summary>Lance un son avec ses réglages : volume, découpe, fondus, boucle.</summary>
    public bool Play(string soundId, float[] data, PlaybackOptions options)
    {
        if (data.Length < 2 || ActiveCount >= MaxSimultaneous) return false;
        options = options.Clamp(data.Length);
        var voice = new Voice(soundId, data, options);
        for (int i = 0; i < _voices.Length; i++)
            if (Interlocked.CompareExchange(ref _voices[i], voice, null) == null) return true;
        return false;
    }

    public void Stop(string soundId)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            var voice = Volatile.Read(ref _voices[i]);
            if (voice != null && voice.SoundId == soundId) voice.StopRequested = true;
        }
    }

    public void StopAll()
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            var voice = Volatile.Read(ref _voices[i]);
            if (voice != null) voice.StopRequested = true;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        int frames = count / 2;

        for (int v = 0; v < _voices.Length; v++)
        {
            var voice = Volatile.Read(ref _voices[v]);
            if (voice == null) continue;

            if (voice.StopRequested && voice.FadeRemaining < 0) voice.FadeRemaining = FadeFrames;

            float[] data = voice.Data;
            var options = voice.Options;
            int position = voice.Position;
            bool finished = false;

            for (int i = 0, o = offset; i < frames; i++, o += 2, position += 2)
            {
                if (position >= options.EndSample)
                {
                    if (!options.Loop || voice.StopRequested)
                    {
                        finished = true;
                        break;
                    }
                    position = options.StartSample; // on repart au début
                }

                float gain = options.Volume * options.EnvelopeAt(position);
                if (voice.FadeRemaining >= 0)
                {
                    gain *= voice.FadeRemaining / (float)FadeFrames;
                    if (voice.FadeRemaining == 0)
                    {
                        finished = true;
                        break;
                    }
                    voice.FadeRemaining--;
                }
                buffer[o] += data[position] * gain;
                buffer[o + 1] += data[position + 1] * gain;
            }

            voice.Position = position;
            if (finished) Interlocked.CompareExchange(ref _voices[v], null, voice);
        }
        return count;
    }
}

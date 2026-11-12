using NAudio.Wave;

namespace VoiceStudio.Audio.Mixing;

/// <summary>
/// Une tranche de la table de mixage (microphone, sons du PC...).
/// Les réglages sont modifiables depuis n'importe quel thread ; le traitement tourne sur le thread audio.
/// </summary>
public sealed class MixerChannel
{
    private const float PeakDecayPerSecond = 0.02f; // le crête-mètre retombe de ~34 dB par seconde

    private readonly float _peakDecayPerFrame;
    private ISampleProvider? _source;
    private float _volume = 1f;
    private bool _muted;
    private bool _solo;
    private bool _mono;
    private float _externalGain = 1f;
    private float _routeGain = 1f;
    private float _currentGain;
    private float _peak;

    public MixerChannel(string name, int sampleRate)
    {
        Name = name;
        _peakDecayPerFrame = MathF.Pow(PeakDecayPerSecond, 1f / sampleRate);
    }

    public string Name { get; }

    /// <summary>Source audio (48 kHz stéréo float). Null = tranche silencieuse.</summary>
    public ISampleProvider? Source
    {
        get => Volatile.Read(ref _source);
        set
        {
            if (value != null &&
                (value.WaveFormat.SampleRate != AudioConstants.SampleRate ||
                 value.WaveFormat.Channels != AudioConstants.Channels))
                throw new ArgumentException("La source doit être en 48 kHz stéréo.", nameof(value));
            Volatile.Write(ref _source, value);
        }
    }

    /// <summary>Volume linéaire : 0 = silence, 1 = niveau d'origine, 2 = +6 dB.</summary>
    public float Volume
    {
        get => Volatile.Read(ref _volume);
        set => Volatile.Write(ref _volume, Math.Clamp(value, 0f, 2f));
    }

    public bool Muted
    {
        get => Volatile.Read(ref _muted);
        set => Volatile.Write(ref _muted, value);
    }

    /// <summary>Écouter cette tranche seule : toutes les autres se taisent.</summary>
    public bool Solo
    {
        get => Volatile.Read(ref _solo);
        set => Volatile.Write(ref _solo, value);
    }

    /// <summary>
    /// Forcer cette source en mono : les deux canaux deviennent identiques.
    /// Utile quand un jeu ou un micro n'envoie le son que d'un côté.
    /// </summary>
    public bool Mono
    {
        get => Volatile.Read(ref _mono);
        set => Volatile.Write(ref _mono, value);
    }

    /// <summary>
    /// Gain supplémentaire piloté par le moteur (auto-ducking, push-to-talk) : 0 à 1.
    /// Se combine avec le volume réglé par l'utilisateur.
    /// </summary>
    public float ExternalGain
    {
        get => Volatile.Read(ref _externalGain);
        set => Volatile.Write(ref _externalGain, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>
    /// Deuxième gain piloté par le moteur, indépendant de <see cref="ExternalGain"/> :
    /// sert à la bascule micro / source, qui ne doit pas se marcher dessus avec l'auto-ducking.
    /// </summary>
    public float RouteGain
    {
        get => Volatile.Read(ref _routeGain);
        set => Volatile.Write(ref _routeGain, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>Niveau crête après volume (ce qui part réellement dans le mix), linéaire.</summary>
    public float Peak => Volatile.Read(ref _peak);

    /// <summary>
    /// Lit la source, applique le volume (avec rampe anti-clic) et additionne dans le mix.
    /// Retourne le niveau crête de ce bloc.
    /// </summary>
    internal float MixInto(float[] mix, int offset, int count, float[] scratch, bool soloActive = false)
    {
        var source = Source;
        float peak = _peak;
        int frames = count / 2;

        if (source == null)
        {
            _currentGain = 0f;
            Volatile.Write(ref _peak, peak * MathF.Pow(_peakDecayPerFrame, frames));
            return 0f;
        }

        int read = source.Read(scratch, 0, count);
        if (read < count) Array.Clear(scratch, read, count - read);

        bool silenced = Muted || (soloActive && !Solo);
        float targetGain = silenced ? 0f : Volume * ExternalGain * RouteGain;
        bool mono = Mono;
        float blockPeak = 0f;
        float gain = _currentGain;
        float step = frames > 0 ? (targetGain - gain) / frames : 0f;

        for (int i = 0, o = offset; i < count - 1; i += 2, o += 2)
        {
            gain += step;
            float sourceLeft = scratch[i];
            float sourceRight = scratch[i + 1];
            if (mono)
            {
                float middle = (sourceLeft + sourceRight) * 0.5f;
                sourceLeft = middle;
                sourceRight = middle;
            }
            float left = sourceLeft * gain;
            float right = sourceRight * gain;
            mix[o] += left;
            mix[o + 1] += right;

            float level = MathF.Max(MathF.Abs(left), MathF.Abs(right));
            if (level > blockPeak) blockPeak = level;
            peak = level > peak ? level : peak * _peakDecayPerFrame;
        }

        _currentGain = targetGain;
        Volatile.Write(ref _peak, peak);
        return blockPeak;
    }
}

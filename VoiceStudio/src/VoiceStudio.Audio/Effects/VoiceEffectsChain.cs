using NAudio.Wave;

namespace VoiceStudio.Audio.Effects;

/// <summary>
/// Chaîne de traitement de la voix : noise gate, puis les effets activés, dans l'ordre.
/// S'insère entre la capture du micro et le mixeur. La voix est traitée en mono puis recopiée en stéréo.
/// </summary>
public sealed class VoiceEffectsChain : ISampleProvider
{
    private readonly VoiceEffect[] _effects;
    private readonly bool[] _wasActive;
    private ISampleProvider? _source;
    private float[] _mono = new float[AudioConstants.SampleRate / 5];
    private bool _effectsEnabled = true;
    private bool _gateWasEnabled;

    public VoiceEffectsChain()
    {
        int rate = AudioConstants.SampleRate;
        Gate = new NoiseGate(rate);
        _effects =
        [
            new PitchShiftEffect(rate),
            new FilterEffect(rate),
            new RobotEffect(rate),
            new DistortionEffect(),
            new RadioEffect(rate),
            new TelephoneEffect(rate),
            new EchoEffect(rate),
            new ReverbEffect(rate)
        ];
        _wasActive = new bool[_effects.Length];
    }

    public WaveFormat WaveFormat => AudioConstants.MixFormat;

    public NoiseGate Gate { get; }

    /// <summary>Les effets, dans l'ordre d'application.</summary>
    public IReadOnlyList<VoiceEffect> Effects => _effects;

    /// <summary>Interrupteur général des effets (le noise gate reste indépendant).</summary>
    public bool EffectsEnabled
    {
        get => Volatile.Read(ref _effectsEnabled);
        set => Volatile.Write(ref _effectsEnabled, value);
    }

    /// <summary>Source 48 kHz stéréo (le micro). Null = silence.</summary>
    public ISampleProvider? Source
    {
        get => Volatile.Read(ref _source);
        set => Volatile.Write(ref _source, value);
    }

    public VoiceEffect? FindEffect(string id)
    {
        foreach (var effect in _effects)
            if (effect.Id == id) return effect;
        return null;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var source = Source;
        if (source == null)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        int read = source.Read(buffer, offset, count);
        if (read < count) Array.Clear(buffer, offset + read, count - read);

        bool gateEnabled = Gate.Enabled;
        bool effectsEnabled = EffectsEnabled;
        bool anyEffect = false;
        for (int e = 0; e < _effects.Length; e++)
        {
            bool active = effectsEnabled && _effects[e].Enabled;
            if (active && !_wasActive[e]) _effects[e].Reset();
            _wasActive[e] = active;
            anyEffect |= active;
        }
        if (gateEnabled && !_gateWasEnabled) Gate.Reset();
        _gateWasEnabled = gateEnabled;

        if (!gateEnabled && !anyEffect) return count; // rien à faire : le signal passe tel quel

        int frames = count / 2;
        if (_mono.Length < frames) _mono = new float[frames]; // exceptionnel
        var mono = _mono.AsSpan(0, frames);
        for (int i = 0, s = offset; i < frames; i++, s += 2)
            mono[i] = (buffer[s] + buffer[s + 1]) * 0.5f;

        if (gateEnabled) Gate.Process(mono);
        for (int e = 0; e < _effects.Length; e++)
            if (_wasActive[e]) _effects[e].Process(mono);

        for (int i = 0, s = offset; i < frames; i++, s += 2)
        {
            float sample = mono[i];
            buffer[s] = sample;
            buffer[s + 1] = sample;
        }
        return count;
    }
}

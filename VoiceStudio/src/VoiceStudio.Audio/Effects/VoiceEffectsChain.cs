using NAudio.Wave;

namespace VoiceStudio.Audio.Effects;

/// <summary>
/// Chaîne de traitement de la voix : suppression du bruit, noise gate, puis les effets activés.
/// S'insère entre la capture du micro et le mixeur. La voix est traitée en mono puis recopiée en stéréo.
/// </summary>
public sealed class VoiceEffectsChain : ISampleProvider
{
    private readonly VoiceEffect[] _effects;
    private readonly bool[] _wasActive;
    private ISampleProvider? _source;
    private float[] _mono = new float[AudioConstants.SampleRate / 5];
    private float _gain = 1f;
    private float _currentGain = 1f;
    private bool _effectsEnabled = true;
    private bool _gateWasEnabled;
    private bool _suppressorWasEnabled;

    public VoiceEffectsChain()
    {
        int rate = AudioConstants.SampleRate;
        Gate = new NoiseGate(rate);
        NoiseSuppression = new NoiseSuppressor(rate);
        AutoLevel = new AutoLevel(rate);
        _effects =
        [
            new PopFilterEffect(rate),
            new CompressorEffect(rate),
            new DeEsserEffect(rate),
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

    /// <summary>Suppression du bruit de fond, avant le noise gate.</summary>
    public NoiseSuppressor NoiseSuppression { get; }

    /// <summary>Niveau automatique, appliqué après le noise gate.</summary>
    public AutoLevel AutoLevel { get; }

    /// <summary>Les effets, dans l'ordre d'application.</summary>
    public IReadOnlyList<VoiceEffect> Effects => _effects;

    /// <summary>
    /// Gain appliqué au micro avant tout traitement : 1 = niveau d'origine, 4 = +12 dB.
    /// Sert aux micros trop discrets, que les correspondants entendent à peine.
    /// </summary>
    public float Gain
    {
        get => Volatile.Read(ref _gain);
        set => Volatile.Write(ref _gain, Math.Clamp(value, 0.25f, 16f));
    }

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

        float targetGain = Gain;
        if (targetGain != 1f || _currentGain != 1f)
        {
            float gain = _currentGain;
            float step = count > 0 ? (targetGain - gain) / count : 0f;
            for (int i = offset; i < offset + count; i++)
            {
                gain += step;
                buffer[i] *= gain;
            }
            _currentGain = targetGain;
        }

        bool suppressorEnabled = NoiseSuppression.Enabled;
        if (suppressorEnabled && !_suppressorWasEnabled) NoiseSuppression.Reset();
        _suppressorWasEnabled = suppressorEnabled;

        bool autoLevelEnabled = AutoLevel.Enabled;
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

        if (!suppressorEnabled && !gateEnabled && !autoLevelEnabled && !anyEffect)
            return count; // rien à faire : le signal passe tel quel

        int frames = count / 2;
        if (_mono.Length < frames) _mono = new float[frames]; // exceptionnel
        var mono = _mono.AsSpan(0, frames);
        for (int i = 0, s = offset; i < frames; i++, s += 2)
            mono[i] = (buffer[s] + buffer[s + 1]) * 0.5f;

        if (suppressorEnabled) NoiseSuppression.Process(mono);
        if (gateEnabled) Gate.Process(mono);
        if (autoLevelEnabled) AutoLevel.Process(mono);
        for (int e = 0; e < _effects.Length; e++)
            if (_wasActive[e]) _effects[e].Apply(mono);

        for (int i = 0, s = offset; i < frames; i++, s += 2)
        {
            float sample = mono[i];
            buffer[s] = sample;
            buffer[s + 1] = sample;
        }
        return count;
    }
}

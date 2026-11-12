namespace VoiceStudio.Audio.Effects;

/// <summary>Filtre réglable : passe-bas (étouffe les aigus) ou passe-haut (retire les graves).</summary>
public sealed class FilterEffect : VoiceEffect
{
    private readonly int _sampleRate;
    private readonly EffectParameter _mode, _cutoff, _resonance;
    private BiQuad _filter;
    private float _lastMode = -1, _lastCutoff = -1, _lastResonance = -1;

    public FilterEffect(int sampleRate) : base("filter", "Filtre",
        new EffectParameter("mode", "Type (0 = passe-bas, 1 = passe-haut)", 0f, 1f, 0f),
        new EffectParameter("cutoff", "Fréquence de coupure", 100f, 8000f, 1200f, "Hz"),
        new EffectParameter("resonance", "Résonance", 0.5f, 6f, 0.8f))
    {
        _sampleRate = sampleRate;
        _mode = Parameters[0];
        _cutoff = Parameters[1];
        _resonance = Parameters[2];
    }

    protected override void Process(Span<float> samples)
    {
        float mode = _mode.Value >= 0.5f ? 1f : 0f, cutoff = _cutoff.Value, resonance = _resonance.Value;
        if (mode != _lastMode || cutoff != _lastCutoff || resonance != _lastResonance)
        {
            if (mode == 0f) _filter.SetLowPass(_sampleRate, cutoff, resonance);
            else _filter.SetHighPass(_sampleRate, cutoff, resonance);
            _lastMode = mode;
            _lastCutoff = cutoff;
            _lastResonance = resonance;
        }
        for (int i = 0; i < samples.Length; i++) samples[i] = _filter.Process(samples[i]);
    }

    public override void Reset() => _filter.Reset();
}

/// <summary>Robot : la voix est multipliée par une onde fixe (modulation en anneau).</summary>
public sealed class RobotEffect : VoiceEffect
{
    private readonly int _sampleRate;
    private readonly EffectParameter _frequency, _mix;
    private float _phase;

    public RobotEffect(int sampleRate) : base("robot", "Robot",
        new EffectParameter("frequency", "Fréquence", 20f, 300f, 60f, "Hz"),
        new EffectParameter("mix", "Intensité", 0f, 1f, 1f))
    {
        _sampleRate = sampleRate;
        _frequency = Parameters[0];
        _mix = Parameters[1];
    }

    protected override void Process(Span<float> samples)
    {
        float step = 2f * MathF.PI * _frequency.Value / _sampleRate;
        float mix = _mix.Value;
        for (int i = 0; i < samples.Length; i++)
        {
            float dry = samples[i];
            samples[i] = dry * (1f - mix) + dry * MathF.Sin(_phase) * mix * 1.6f;
            _phase += step;
            if (_phase > 2f * MathF.PI) _phase -= 2f * MathF.PI;
        }
    }

    public override void Reset() => _phase = 0f;
}

/// <summary>Distorsion : saturation douce (tanh), niveau de sortie compensé.</summary>
public sealed class DistortionEffect : VoiceEffect
{
    private readonly EffectParameter _drive, _level;

    public DistortionEffect() : base("distortion", "Distorsion",
        new EffectParameter("drive", "Saturation", 1f, 40f, 12f),
        new EffectParameter("level", "Niveau de sortie", 0.1f, 1f, 0.6f))
    {
        _drive = Parameters[0];
        _level = Parameters[1];
    }

    protected override void Process(Span<float> samples)
    {
        float drive = _drive.Value, level = _level.Value;
        for (int i = 0; i < samples.Length; i++) samples[i] = MathF.Tanh(samples[i] * drive) * level;
    }
}

/// <summary>Radio : bande de fréquences étroite + légère saturation, comme un talkie-walkie.</summary>
public sealed class RadioEffect : VoiceEffect
{
    private readonly EffectParameter _drive;
    private BiQuad _highPass, _lowPass;

    public RadioEffect(int sampleRate) : base("radio", "Radio",
        new EffectParameter("drive", "Grésillement", 1f, 12f, 4f))
    {
        _drive = Parameters[0];
        _highPass.SetHighPass(sampleRate, 500f, 0.9f);
        _lowPass.SetLowPass(sampleRate, 2800f, 0.9f);
    }

    protected override void Process(Span<float> samples)
    {
        float drive = _drive.Value;
        float makeup = 1.4f / MathF.Sqrt(drive);
        for (int i = 0; i < samples.Length; i++)
        {
            float band = _lowPass.Process(_highPass.Process(samples[i]));
            samples[i] = MathF.Tanh(band * drive) * makeup;
        }
    }

    public override void Reset()
    {
        _highPass.Reset();
        _lowPass.Reset();
    }
}

/// <summary>Téléphone : bande 300–3400 Hz très marquée, comme une ligne téléphonique.</summary>
public sealed class TelephoneEffect : VoiceEffect
{
    private BiQuad _highPass1, _highPass2, _lowPass1, _lowPass2;

    public TelephoneEffect(int sampleRate) : base("telephone", "Téléphone")
    {
        _highPass1.SetHighPass(sampleRate, 300f, 0.8f);
        _highPass2.SetHighPass(sampleRate, 300f, 0.8f);
        _lowPass1.SetLowPass(sampleRate, 3400f, 0.8f);
        _lowPass2.SetLowPass(sampleRate, 3400f, 0.8f);
    }

    protected override void Process(Span<float> samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float x = _highPass2.Process(_highPass1.Process(samples[i]));
            samples[i] = _lowPass2.Process(_lowPass1.Process(x)) * 1.3f;
        }
    }

    public override void Reset()
    {
        _highPass1.Reset();
        _highPass2.Reset();
        _lowPass1.Reset();
        _lowPass2.Reset();
    }
}

/// <summary>Écho : répétitions de la voix, de plus en plus faibles.</summary>
public sealed class EchoEffect : VoiceEffect
{
    private readonly int _sampleRate;
    private readonly float[] _buffer;
    private readonly EffectParameter _delay, _feedback, _mix;
    private int _write;

    public EchoEffect(int sampleRate) : base("echo", "Écho",
        new EffectParameter("delay", "Délai", 50f, 900f, 280f, "ms"),
        new EffectParameter("feedback", "Répétitions", 0f, 0.85f, 0.4f),
        new EffectParameter("mix", "Intensité", 0f, 1f, 0.45f))
    {
        _sampleRate = sampleRate;
        _buffer = new float[sampleRate]; // 1 seconde
        _delay = Parameters[0];
        _feedback = Parameters[1];
        _mix = Parameters[2];
    }

    protected override void Process(Span<float> samples)
    {
        int delaySamples = Math.Clamp((int)(_delay.Value * _sampleRate / 1000f), 1, _buffer.Length - 1);
        float feedback = _feedback.Value, mix = _mix.Value;
        for (int i = 0; i < samples.Length; i++)
        {
            int read = _write - delaySamples;
            if (read < 0) read += _buffer.Length;
            float delayed = _buffer[read];
            float dry = samples[i];
            _buffer[_write] = dry + delayed * feedback;
            samples[i] = dry + delayed * mix;
            if (++_write == _buffer.Length) _write = 0;
        }
    }

    public override void Reset() => Array.Clear(_buffer);
}

/// <summary>Réverbération : simule une pièce (4 filtres en peigne + 2 passe-tout, type Schroeder).</summary>
public sealed class ReverbEffect : VoiceEffect
{
    private static readonly int[] CombLengths = [1116, 1188, 1277, 1356];
    private static readonly int[] AllPassLengths = [556, 441];

    private readonly float[][] _combs;
    private readonly int[] _combIndex = new int[4];
    private readonly float[] _combFilter = new float[4];
    private readonly float[][] _allPasses;
    private readonly int[] _allPassIndex = new int[2];
    private readonly EffectParameter _room, _mix;

    public ReverbEffect(int sampleRate) : base("reverb", "Réverbération",
        new EffectParameter("room", "Taille de la pièce", 0f, 1f, 0.6f),
        new EffectParameter("mix", "Intensité", 0f, 1f, 0.35f))
    {
        float scale = sampleRate / 44100f;
        _combs = CombLengths.Select(length => new float[(int)(length * scale)]).ToArray();
        _allPasses = AllPassLengths.Select(length => new float[(int)(length * scale)]).ToArray();
        _room = Parameters[0];
        _mix = Parameters[1];
    }

    protected override void Process(Span<float> samples)
    {
        float feedback = 0.70f + 0.28f * _room.Value;
        float mix = _mix.Value;
        const float damping = 0.25f;

        for (int i = 0; i < samples.Length; i++)
        {
            float dry = samples[i];
            float input = dry * 0.25f;
            float wet = 0f;

            for (int c = 0; c < 4; c++)
            {
                float[] comb = _combs[c];
                int index = _combIndex[c];
                float output = comb[index];
                _combFilter[c] = output * (1f - damping) + _combFilter[c] * damping;
                comb[index] = input + _combFilter[c] * feedback;
                if (++index == comb.Length) index = 0;
                _combIndex[c] = index;
                wet += output;
            }

            for (int a = 0; a < 2; a++)
            {
                float[] allPass = _allPasses[a];
                int index = _allPassIndex[a];
                float buffered = allPass[index];
                float output = buffered - wet;
                allPass[index] = wet + buffered * 0.5f;
                if (++index == allPass.Length) index = 0;
                _allPassIndex[a] = index;
                wet = output;
            }

            if (MathF.Abs(wet) < 1e-20f) wet = 0f;
            samples[i] = dry * (1f - mix * 0.5f) + wet * mix;
        }
    }

    public override void Reset()
    {
        foreach (var comb in _combs) Array.Clear(comb);
        foreach (var allPass in _allPasses) Array.Clear(allPass);
        Array.Clear(_combFilter);
    }
}

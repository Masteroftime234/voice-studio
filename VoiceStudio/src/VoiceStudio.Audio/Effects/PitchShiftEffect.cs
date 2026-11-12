namespace VoiceStudio.Audio.Effects;

/// <summary>
/// Voix grave / aiguë : change la hauteur SANS changer la vitesse de parole.
/// Deux lectures décalées dans une ligne à retard, fondues l'une dans l'autre (~45 ms de fenêtre) :
/// faible latence et faible coût processeur, adapté au temps réel.
/// </summary>
public sealed class PitchShiftEffect : VoiceEffect
{
    private const int BufferSize = 8192;
    private const int Mask = BufferSize - 1;

    private readonly float[] _buffer = new float[BufferSize];
    private readonly float _window;
    private readonly EffectParameter _semitones;
    private int _write;
    private float _phase;

    public PitchShiftEffect(int sampleRate) : base("pitch", "Hauteur de la voix",
        new EffectParameter("semitones", "Hauteur", -12f, 12f, -4f, "demi-tons"))
    {
        _window = sampleRate * 0.045f;
        _semitones = Parameters[0];
    }

    protected override void Process(Span<float> samples)
    {
        float ratio = MathF.Pow(2f, _semitones.Value / 12f);
        float rate = (1f - ratio) / _window;

        for (int i = 0; i < samples.Length; i++)
        {
            _buffer[_write] = samples[i];

            _phase += rate;
            _phase -= MathF.Floor(_phase);
            float phase2 = _phase + 0.5f;
            if (phase2 >= 1f) phase2 -= 1f;

            float sin = MathF.Sin(MathF.PI * _phase);
            float gain1 = sin * sin;
            samples[i] = gain1 * Tap(_phase * _window) + (1f - gain1) * Tap(phase2 * _window);

            _write = (_write + 1) & Mask;
        }
    }

    private float Tap(float delay)
    {
        float position = _write - 1 - delay;
        float floor = MathF.Floor(position);
        float fraction = position - floor;
        int index = (int)floor;
        float a = _buffer[index & Mask];
        float b = _buffer[(index + 1) & Mask];
        return a + (b - a) * fraction;
    }

    public override void Reset()
    {
        Array.Clear(_buffer);
        _phase = 0f;
    }
}

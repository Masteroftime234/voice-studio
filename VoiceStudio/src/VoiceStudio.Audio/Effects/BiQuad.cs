namespace VoiceStudio.Audio.Effects;

/// <summary>Filtre du second ordre (formules « Audio EQ Cookbook » de R. Bristow-Johnson).</summary>
public struct BiQuad
{
    private float _b0, _b1, _b2, _a1, _a2;
    private float _x1, _x2, _y1, _y2;

    public void SetLowPass(float sampleRate, float frequency, float q) => Set(sampleRate, frequency, q, 0);
    public void SetHighPass(float sampleRate, float frequency, float q) => Set(sampleRate, frequency, q, 1);
    public void SetBandPass(float sampleRate, float frequency, float q) => Set(sampleRate, frequency, q, 2);

    private void Set(float sampleRate, float frequency, float q, int type)
    {
        frequency = Math.Clamp(frequency, 20f, sampleRate * 0.45f);
        q = MathF.Max(q, 0.1f);
        float w0 = 2f * MathF.PI * frequency / sampleRate;
        float cos = MathF.Cos(w0);
        float alpha = MathF.Sin(w0) / (2f * q);
        float a0 = 1f + alpha;

        switch (type)
        {
            case 0:
                _b0 = (1f - cos) / 2f / a0;
                _b1 = (1f - cos) / a0;
                _b2 = _b0;
                break;
            case 1:
                _b0 = (1f + cos) / 2f / a0;
                _b1 = -(1f + cos) / a0;
                _b2 = _b0;
                break;
            default:
                _b0 = alpha / a0;
                _b1 = 0f;
                _b2 = -alpha / a0;
                break;
        }
        _a1 = -2f * cos / a0;
        _a2 = (1f - alpha) / a0;
    }

    public float Process(float x)
    {
        float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        if (MathF.Abs(y) < 1e-20f) y = 0f; // évite les nombres dénormalisés (lents)
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }

    public void Reset() => _x1 = _x2 = _y1 = _y2 = 0f;
}

namespace VoiceStudio.Audio.Processing;

/// <summary>
/// Limiteur de sortie : protège contre la saturation quand la somme des sources dépasse 0 dBFS.
/// Attaque instantanée, relâchement progressif, puis écrêtage de sécurité à ±1.
/// </summary>
public sealed class SoftLimiter
{
    private readonly float _threshold;
    private readonly float _releaseCoefficient;
    private float _gain = 1f;

    public SoftLimiter(int sampleRate, float threshold = 0.89f, float releaseMilliseconds = 120f)
    {
        _threshold = threshold;
        _releaseCoefficient = 1f - MathF.Exp(-1f / (sampleRate * releaseMilliseconds / 1000f));
    }

    /// <summary>Réduction de gain actuelle (1 = aucune réduction).</summary>
    public float CurrentGain => Volatile.Read(ref _gain);

    public void Process(float[] buffer, int offset, int count)
    {
        float gain = _gain;
        int end = offset + count - 1;
        for (int i = offset; i < end; i += 2)
        {
            float left = buffer[i];
            float right = buffer[i + 1];
            float peak = MathF.Max(MathF.Abs(left), MathF.Abs(right));
            float target = peak > _threshold ? _threshold / peak : 1f;

            if (target < gain) gain = target;
            else gain += (target - gain) * _releaseCoefficient;

            buffer[i] = Math.Clamp(left * gain, -1f, 1f);
            buffer[i + 1] = Math.Clamp(right * gain, -1f, 1f);
        }
        Volatile.Write(ref _gain, gain);
    }
}

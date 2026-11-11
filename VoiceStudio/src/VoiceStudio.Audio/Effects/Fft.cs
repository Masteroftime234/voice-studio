namespace VoiceStudio.Audio.Effects;

/// <summary>Transformée de Fourier rapide (radix-2), sur place, sans allocation.</summary>
internal static class Fft
{
    /// <summary>Transforme en place. <paramref name="inverse"/> applique la transformée inverse normalisée.</summary>
    public static void Transform(float[] real, float[] imaginary, bool inverse)
    {
        int n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            float angle = 2f * MathF.PI / length * (inverse ? 1f : -1f);
            float stepReal = MathF.Cos(angle), stepImaginary = MathF.Sin(angle);
            for (int start = 0; start < n; start += length)
            {
                float wReal = 1f, wImaginary = 0f;
                for (int k = 0; k < length / 2; k++)
                {
                    int a = start + k, b = a + length / 2;
                    float tReal = real[b] * wReal - imaginary[b] * wImaginary;
                    float tImaginary = real[b] * wImaginary + imaginary[b] * wReal;
                    real[b] = real[a] - tReal;
                    imaginary[b] = imaginary[a] - tImaginary;
                    real[a] += tReal;
                    imaginary[a] += tImaginary;
                    float nextReal = wReal * stepReal - wImaginary * stepImaginary;
                    wImaginary = wReal * stepImaginary + wImaginary * stepReal;
                    wReal = nextReal;
                }
            }
        }

        if (!inverse) return;
        float scale = 1f / n;
        for (int i = 0; i < n; i++)
        {
            real[i] *= scale;
            imaginary[i] *= scale;
        }
    }
}

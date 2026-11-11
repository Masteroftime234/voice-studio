using System.Runtime.InteropServices;
using NAudio.Wave;

namespace VoiceStudio.Audio.Processing;

public enum SampleKind
{
    Float32,
    Pcm16,
    Pcm24,
    Pcm32
}

/// <summary>Conversion des octets bruts d'un périphérique en échantillons float [-1 ; 1].</summary>
public static class SampleConverter
{
    private static readonly Guid SubTypePcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid SubTypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>Détermine le type d'échantillon d'un format WASAPI.</summary>
    public static SampleKind GetSampleKind(WaveFormat format)
    {
        bool isFloat;
        if (format is WaveFormatExtensible extensible)
        {
            if (extensible.SubFormat == SubTypeIeeeFloat) isFloat = true;
            else if (extensible.SubFormat == SubTypePcm) isFloat = false;
            else throw new NotSupportedException($"Format audio non pris en charge : {extensible.SubFormat}");
        }
        else if (format.Encoding == WaveFormatEncoding.IeeeFloat) isFloat = true;
        else if (format.Encoding == WaveFormatEncoding.Pcm) isFloat = false;
        else throw new NotSupportedException($"Format audio non pris en charge : {format.Encoding}");

        return (isFloat, format.BitsPerSample) switch
        {
            (true, 32) => SampleKind.Float32,
            (false, 16) => SampleKind.Pcm16,
            (false, 24) => SampleKind.Pcm24,
            (false, 32) => SampleKind.Pcm32,
            _ => throw new NotSupportedException(
                $"Format audio non pris en charge : {format.BitsPerSample} bits ({format.Encoding})")
        };
    }

    public static int BytesPerSample(SampleKind kind) => kind switch
    {
        SampleKind.Pcm16 => 2,
        SampleKind.Pcm24 => 3,
        _ => 4
    };

    /// <summary>
    /// Convertit <paramref name="source"/> vers <paramref name="destination"/>.
    /// Retourne le nombre d'échantillons écrits. N'alloue rien.
    /// </summary>
    public static int ToFloat(ReadOnlySpan<byte> source, SampleKind kind, Span<float> destination)
    {
        int count = Math.Min(source.Length / BytesPerSample(kind), destination.Length);
        switch (kind)
        {
            case SampleKind.Float32:
                MemoryMarshal.Cast<byte, float>(source).Slice(0, count).CopyTo(destination);
                break;

            case SampleKind.Pcm16:
            {
                var samples = MemoryMarshal.Cast<byte, short>(source);
                for (int i = 0; i < count; i++) destination[i] = samples[i] * (1f / 32768f);
                break;
            }

            case SampleKind.Pcm24:
                for (int i = 0, b = 0; i < count; i++, b += 3)
                {
                    int value = source[b] | (source[b + 1] << 8) | ((sbyte)source[b + 2] << 16);
                    destination[i] = value * (1f / 8388608f);
                }
                break;

            case SampleKind.Pcm32:
            {
                var samples = MemoryMarshal.Cast<byte, int>(source);
                for (int i = 0; i < count; i++) destination[i] = samples[i] * (1f / 2147483648f);
                break;
            }
        }
        return count;
    }
}

using NAudio.Wave;

namespace VoiceStudio.Audio.Routing;

/// <summary>
/// Laisse passer le signal et en envoie une copie dans un tampon (pour le retour audio).
/// La copie n'est faite que si <see cref="CopyEnabled"/> est vrai.
/// </summary>
public sealed class TeeSampleProvider(ISampleProvider source, AudioRingBuffer copy) : ISampleProvider
{
    private bool _copyEnabled;

    public WaveFormat WaveFormat => source.WaveFormat;

    public bool CopyEnabled
    {
        get => Volatile.Read(ref _copyEnabled);
        set => Volatile.Write(ref _copyEnabled, value);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (CopyEnabled) copy.Write(buffer.AsSpan(offset, read));
        return read;
    }
}

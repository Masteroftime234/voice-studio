namespace VoiceStudio.Audio.Routing;

/// <summary>
/// File circulaire d'échantillons float, sans verrou, pour UN producteur (thread de capture)
/// et UN consommateur (thread de sortie). Aucune allocation après la construction.
/// </summary>
public sealed class AudioRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _mask;
    private long _writePos; // modifié uniquement par le producteur
    private long _readPos;  // modifié uniquement par le consommateur

    public AudioRingBuffer(int minimumCapacity)
    {
        if (minimumCapacity < 2) throw new ArgumentOutOfRangeException(nameof(minimumCapacity));
        int capacity = 1;
        while (capacity < minimumCapacity) capacity <<= 1;
        _buffer = new float[capacity];
        _mask = capacity - 1;
    }

    public int Capacity => _buffer.Length;

    /// <summary>Nombre d'échantillons disponibles en lecture.</summary>
    public int Available => (int)(Volatile.Read(ref _writePos) - Volatile.Read(ref _readPos));

    /// <summary>Écrit autant d'échantillons que possible. Retourne le nombre réellement écrit.</summary>
    public int Write(ReadOnlySpan<float> source)
    {
        long write = _writePos;
        long read = Volatile.Read(ref _readPos);
        int free = _buffer.Length - (int)(write - read);
        int count = Math.Min(free, source.Length);
        if (count <= 0) return 0;

        int index = (int)(write & _mask);
        int first = Math.Min(count, _buffer.Length - index);
        source.Slice(0, first).CopyTo(_buffer.AsSpan(index, first));
        if (count > first)
            source.Slice(first, count - first).CopyTo(_buffer.AsSpan(0, count - first));

        Volatile.Write(ref _writePos, write + count);
        return count;
    }

    /// <summary>Lit jusqu'à destination.Length échantillons. Retourne le nombre réellement lu.</summary>
    public int Read(Span<float> destination)
    {
        long read = _readPos;
        long write = Volatile.Read(ref _writePos);
        int count = Math.Min((int)(write - read), destination.Length);
        if (count <= 0) return 0;

        int index = (int)(read & _mask);
        int first = Math.Min(count, _buffer.Length - index);
        _buffer.AsSpan(index, first).CopyTo(destination);
        if (count > first)
            _buffer.AsSpan(0, count - first).CopyTo(destination.Slice(first));

        Volatile.Write(ref _readPos, read + count);
        return count;
    }

    /// <summary>Jette des échantillons (côté consommateur uniquement).</summary>
    public int Skip(int count)
    {
        long read = _readPos;
        long write = Volatile.Read(ref _writePos);
        count = Math.Min((int)(write - read), count);
        if (count <= 0) return 0;
        Volatile.Write(ref _readPos, read + count);
        return count;
    }

    /// <summary>Vide le tampon (côté consommateur uniquement).</summary>
    public void Clear() => Volatile.Write(ref _readPos, Volatile.Read(ref _writePos));
}

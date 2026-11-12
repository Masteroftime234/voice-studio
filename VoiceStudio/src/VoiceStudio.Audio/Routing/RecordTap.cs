using NAudio.Wave;

namespace VoiceStudio.Audio.Routing;

/// <summary>
/// Laisse passer le signal et, quand on le lui demande, en garde une copie :
/// sert au test du micro, qui réécoute ce que les autres entendent de toi.
/// </summary>
public sealed class RecordTap(ISampleProvider source) : ISampleProvider
{
    private float[]? _buffer;
    private int _written;
    private volatile bool _recording;

    public WaveFormat WaveFormat => source.WaveFormat;

    public bool IsRecording => _recording;

    /// <summary>Commence un enregistrement d'au plus <paramref name="seconds"/> secondes.</summary>
    public void Start(int seconds)
    {
        int samples = seconds * WaveFormat.SampleRate * WaveFormat.Channels;
        var buffer = new float[samples];
        _written = 0;
        _buffer = buffer;
        _recording = true;
    }

    /// <summary>Arrête l'enregistrement et retourne ce qui a été capté.</summary>
    public float[] Stop()
    {
        _recording = false;
        var buffer = _buffer;
        _buffer = null;
        if (buffer == null) return [];
        int written = Math.Min(_written, buffer.Length);
        var result = new float[written];
        Array.Copy(buffer, result, written);
        return result;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (!_recording) return read;

        var destination = _buffer;
        if (destination == null) return read;

        int room = Math.Min(read, destination.Length - _written);
        if (room <= 0)
        {
            _recording = false; // durée maximale atteinte
            return read;
        }
        Array.Copy(buffer, offset, destination, _written, room);
        _written += room;
        return read;
    }
}

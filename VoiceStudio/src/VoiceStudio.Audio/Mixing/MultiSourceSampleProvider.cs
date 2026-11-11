using NAudio.Wave;

namespace VoiceStudio.Audio.Mixing;

/// <summary>
/// Additionne un ensemble de sources qui change en cours de route (applications qui démarrent / s'arrêtent).
/// La liste est remplacée d'un bloc : le thread audio ne prend jamais de verrou.
/// </summary>
public sealed class MultiSourceSampleProvider : ISampleProvider
{
    private ISampleProvider[] _sources = [];
    private float[] _scratch = new float[AudioConstants.SampleRate / 5 * AudioConstants.Channels];

    public WaveFormat WaveFormat => AudioConstants.MixFormat;

    /// <summary>Remplace la liste des sources (appelé hors du thread audio).</summary>
    public void SetSources(ISampleProvider[] sources) => Volatile.Write(ref _sources, sources);

    public int Read(float[] buffer, int offset, int count)
    {
        var sources = Volatile.Read(ref _sources);
        Array.Clear(buffer, offset, count);
        if (sources.Length == 0) return count;
        if (_scratch.Length < count) _scratch = new float[count]; // exceptionnel

        for (int s = 0; s < sources.Length; s++)
        {
            int read = sources[s].Read(_scratch, 0, count);
            for (int i = 0; i < read; i++) buffer[offset + i] += _scratch[i];
        }
        return count;
    }
}

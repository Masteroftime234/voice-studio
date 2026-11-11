using NAudio.Wave;
using VoiceStudio.Audio.Capture;

namespace VoiceStudio.Audio.Mixing;

/// <summary>
/// Le son d'une application capturée (Spotify, le navigateur, un jeu...), avec son propre
/// volume, son bouton couper et son indicateur de niveau — une tranche de la table de mixage.
/// </summary>
public sealed class AppAudioSource : ISampleProvider
{
    private readonly CaptureSourcePipeline _pipeline;
    private readonly float _peakDecayPerFrame;
    private float _volume = 1f;
    private bool _muted;
    private float _currentGain;
    private float _peak;

    internal AppAudioSource(string name, int processId, CaptureSourcePipeline pipeline)
    {
        Name = name;
        ProcessId = processId;
        _pipeline = pipeline;
        _peakDecayPerFrame = MathF.Pow(0.02f, 1f / AudioConstants.SampleRate);
    }

    /// <summary>Nom du processus, tel que Windows le donne (« Spotify », « chrome »...).</summary>
    public string Name { get; }

    public int ProcessId { get; }

    public WaveFormat WaveFormat => AudioConstants.MixFormat;

    /// <summary>Volume linéaire : 0 = silence, 1 = niveau d'origine, 1,5 = +3,5 dB.</summary>
    public float Volume
    {
        get => Volatile.Read(ref _volume);
        set => Volatile.Write(ref _volume, Math.Clamp(value, 0f, 2f));
    }

    public bool Muted
    {
        get => Volatile.Read(ref _muted);
        set => Volatile.Write(ref _muted, value);
    }

    /// <summary>Niveau crête après volume, linéaire.</summary>
    public float Peak => Volatile.Read(ref _peak);

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _pipeline.Output.Read(buffer, offset, count);
        if (read < count) Array.Clear(buffer, offset + read, count - read);

        float target = Muted ? 0f : Volume;
        float gain = _currentGain;
        int frames = count / 2;
        float step = frames > 0 ? (target - gain) / frames : 0f;
        float peak = _peak;

        for (int i = offset; i < offset + count - 1; i += 2)
        {
            gain += step;
            float left = buffer[i] * gain;
            float right = buffer[i + 1] * gain;
            buffer[i] = left;
            buffer[i + 1] = right;
            float level = MathF.Max(MathF.Abs(left), MathF.Abs(right));
            peak = level > peak ? level : peak * _peakDecayPerFrame;
        }

        _currentGain = target;
        Volatile.Write(ref _peak, peak);
        return count;
    }

    internal void Write(ReadOnlySpan<byte> data, Processing.SampleKind kind) => _pipeline.Write(data, kind);
}

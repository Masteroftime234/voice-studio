using System.Collections.Concurrent;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceStudio.Audio.Mixing;
using VoiceStudio.Audio.Processing;

namespace VoiceStudio.Audio.Services;

/// <summary>
/// Soundboard : décode les fichiers audio (WAV, MP3 et autres formats lus par Windows)
/// en mémoire, puis les joue dans le mix. Le décodage se fait hors du thread audio.
/// </summary>
public sealed class SoundboardService
{
    private const int MaxSeconds = 600; // 10 minutes par son au maximum
    private readonly ConcurrentDictionary<string, float[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SoundboardMixer Mixer { get; } = new();

    /// <summary>Charge (ou recharge depuis le cache) un fichier. Lève une exception si le fichier est illisible.</summary>
    public Task<float[]> LoadAsync(string path) =>
        _cache.TryGetValue(path, out var cached)
            ? Task.FromResult(cached)
            : Task.Run(() => _cache.GetOrAdd(path, Decode));

    /// <summary>Joue un fichier. Retourne false si la limite de sons simultanés est atteinte.</summary>
    public async Task<bool> PlayAsync(string soundId, string path, float volume) =>
        await PlayAsync(soundId, path, new PlaybackOptions { Volume = volume }).ConfigureAwait(false);

    /// <summary>Joue un fichier avec ses réglages (découpe, fondus, boucle).</summary>
    public async Task<bool> PlayAsync(string soundId, string path, PlaybackOptions options)
    {
        float[] data = await LoadAsync(path).ConfigureAwait(false);
        return Mixer.Play(soundId, data, options);
    }

    /// <summary>Durée d'un fichier déjà chargé, en secondes (0 s'il est inconnu).</summary>
    public double DurationOf(string path) =>
        _cache.TryGetValue(path, out var data)
            ? data.Length / (double)(AudioConstants.SampleRate * AudioConstants.Channels)
            : 0;

    public void Stop(string soundId) => Mixer.Stop(soundId);

    public void StopAll() => Mixer.StopAll();

    public void Forget(string path) => _cache.TryRemove(path, out _);

    /// <summary>Décode un fichier en 48 kHz stéréo float.</summary>
    public static float[] Decode(string path)
    {
        using WaveStream reader = Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)
            ? new WaveFileReader(path)
            : new MediaFoundationReader(path);
        return ReadAll(reader.ToSampleProvider());
    }

    /// <summary>Convertit n'importe quelle source en 48 kHz stéréo et la lit entièrement.</summary>
    public static float[] ReadAll(ISampleProvider source)
    {
        ISampleProvider chain = new StereoAdapterSampleProvider(source);
        if (chain.WaveFormat.SampleRate != AudioConstants.SampleRate)
            chain = new WdlResamplingSampleProvider(chain, AudioConstants.SampleRate);

        int maxSamples = MaxSeconds * AudioConstants.SampleRate * AudioConstants.Channels;
        var result = new List<float>(AudioConstants.SampleRate * AudioConstants.Channels * 4);
        var block = new float[AudioConstants.SampleRate * AudioConstants.Channels / 2];
        int read;
        while (result.Count < maxSamples && (read = chain.Read(block, 0, block.Length)) > 0)
            result.AddRange(block.AsSpan(0, read));

        if (result.Count % 2 != 0) result.RemoveAt(result.Count - 1);
        return result.ToArray();
    }
}

using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceStudio.Audio.Services;

/// <summary>
/// Joue une fois un extrait déjà en mémoire sur un périphérique donné, sans passer
/// par le mix envoyé à Discord : sert à se réécouter pendant le test du micro.
/// </summary>
public sealed class PreviewPlayer : IDisposable
{
    private readonly object _gate = new();
    private WasapiOut? _output;
    private MMDevice? _device;

    public bool IsPlaying { get; private set; }

    /// <summary>Joue l'extrait et rend la main quand il est terminé ou interrompu.</summary>
    public Task PlayAsync(MMDevice device, float[] samples, int latencyMilliseconds = 60)
    {
        Stop();
        if (samples.Length == 0)
        {
            device.Dispose();
            return Task.CompletedTask;
        }

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new WasapiOut(device, AudioClientShareMode.Shared, true, latencyMilliseconds);

        void OnStopped(object? sender, StoppedEventArgs e)
        {
            output.PlaybackStopped -= OnStopped;
            lock (_gate)
            {
                if (ReferenceEquals(_output, output))
                {
                    IsPlaying = false;
                    _output = null;
                    _device?.Dispose();
                    _device = null;
                }
            }
            try { output.Dispose(); } catch { }
            finished.TrySetResult();
        }

        try
        {
            output.PlaybackStopped += OnStopped;
            output.Init(new SampleToWaveProvider(new ArraySampleProvider(samples)));
            output.Play();
        }
        catch
        {
            output.PlaybackStopped -= OnStopped;
            output.Dispose();
            device.Dispose();
            throw;
        }

        lock (_gate)
        {
            _output = output;
            _device = device;
            IsPlaying = true;
        }
        return finished.Task;
    }

    public void Stop()
    {
        WasapiOut? output;
        lock (_gate)
        {
            output = _output;
            _output = null;
            IsPlaying = false;
            _device?.Dispose();
            _device = null;
        }
        if (output == null) return;
        try { output.Stop(); } catch { }
        try { output.Dispose(); } catch { }
    }

    public void Dispose() => Stop();

    /// <summary>Lit un tableau d'échantillons 48 kHz stéréo une seule fois.</summary>
    private sealed class ArraySampleProvider(float[] samples) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat => AudioConstants.MixFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = Math.Min(count, samples.Length - _position);
            if (read <= 0) return 0;
            Array.Copy(samples, _position, buffer, offset, read);
            _position += read;
            return read;
        }
    }
}

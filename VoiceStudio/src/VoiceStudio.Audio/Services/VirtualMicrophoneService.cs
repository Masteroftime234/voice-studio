using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceStudio.Audio.Services;

/// <summary>
/// Envoie le mix vers le périphérique de sortie virtuel (ex. « CABLE Input » de VB-CABLE).
/// Discord lit ensuite l'autre extrémité du câble (« CABLE Output ») comme un microphone.
/// </summary>
public sealed class VirtualMicrophoneService : IDisposable
{
    private readonly object _gate = new();
    private WasapiOut? _output;
    private MMDevice? _device;

    /// <summary>La sortie s'est arrêtée toute seule (périphérique retiré, erreur pilote...).</summary>
    public event Action<Exception>? Faulted;

    public bool IsRunning { get; private set; }

    /// <param name="eventSync">
    /// Faux : Voice Studio pousse les données à intervalle régulier plutôt que d'attendre
    /// le signal du pilote. Plus tolérant avec les câbles audio virtuels.
    /// </param>
    public void Start(MMDevice device, ISampleProvider mix, int latencyMilliseconds, bool eventSync = true)
    {
        lock (_gate)
        {
            StopCore();

            var output = new WasapiOut(device, AudioClientShareMode.Shared, eventSync, latencyMilliseconds);
            try
            {
                output.PlaybackStopped += OnPlaybackStopped;
                output.Init(new SampleToWaveProvider(mix));
                output.Play();
            }
            catch
            {
                output.PlaybackStopped -= OnPlaybackStopped;
                output.Dispose();
                throw;
            }

            _output = output;
            _device = device;
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_gate) StopCore();
    }

    private void StopCore()
    {
        var output = _output;
        _output = null;
        IsRunning = false;
        if (output != null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            try { output.Stop(); } catch { }
            try { output.Dispose(); } catch { /* périphérique déjà retiré */ }
        }
        _device?.Dispose();
        _device = null;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!IsRunning) return;
        IsRunning = false;
        Faulted?.Invoke(e.Exception ?? new InvalidOperationException("La sortie virtuelle s'est arrêtée."));
    }

    public void Dispose() => Stop();
}

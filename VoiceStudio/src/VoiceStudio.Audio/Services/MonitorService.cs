using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceStudio.Audio.Routing;

namespace VoiceStudio.Audio.Services;

/// <summary>
/// Retour audio : permet d'entendre sa propre voix (avec les effets) dans son casque.
/// Désactivé par défaut.
/// </summary>
public sealed class MonitorService : IDisposable
{
    private readonly object _gate = new();
    private readonly AudioRingBuffer _ring = new(AudioConstants.SampleRate * AudioConstants.Channels);
    private readonly VolumeSampleProvider _volume;
    private TeeSampleProvider? _tee;
    private WasapiOut? _output;
    private MMDevice? _device;

    public MonitorService()
    {
        _volume = new VolumeSampleProvider(
            new RingBufferSampleProvider(_ring, AudioConstants.MixFormat, 20, 120)) { Volume = 1f };
    }

    public bool IsRunning { get; private set; }

    public float Volume
    {
        get => _volume.Volume;
        set => _volume.Volume = Math.Clamp(value, 0f, 2f);
    }

    /// <summary>Insère la prise de retour dans la chaîne du micro. À appeler une seule fois.</summary>
    public ISampleProvider Attach(ISampleProvider voice)
    {
        _tee = new TeeSampleProvider(voice, _ring);
        return _tee;
    }

    public void Start(MMDevice device, int latencyMilliseconds)
    {
        lock (_gate)
        {
            StopCore();
            var output = new WasapiOut(device, AudioClientShareMode.Shared, true, latencyMilliseconds);
            try
            {
                _ring.Clear();
                output.Init(new SampleToWaveProvider(_volume));
                output.Play();
            }
            catch
            {
                output.Dispose();
                throw;
            }
            _output = output;
            _device = device;
            if (_tee != null) _tee.CopyEnabled = true;
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_gate) StopCore();
    }

    private void StopCore()
    {
        if (_tee != null) _tee.CopyEnabled = false;
        var output = _output;
        _output = null;
        IsRunning = false;
        if (output != null)
        {
            try { output.Stop(); } catch { }
            try { output.Dispose(); } catch { /* périphérique déjà retiré */ }
        }
        _device?.Dispose();
        _device = null;
    }

    public void Dispose() => Stop();
}

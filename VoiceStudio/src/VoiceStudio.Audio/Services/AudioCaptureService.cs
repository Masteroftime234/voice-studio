using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceStudio.Audio.Capture;
using VoiceStudio.Audio.Processing;

namespace VoiceStudio.Audio.Services;

/// <summary>Capture du microphone physique (WASAPI, mode partagé, piloté par événements).</summary>
public sealed class AudioCaptureService : IDisposable
{
    private readonly object _gate = new();
    private WasapiCapture? _capture;
    private MMDevice? _device;
    private CaptureSourcePipeline? _pipeline;
    private SampleKind _sampleKind;

    /// <summary>La capture s'est arrêtée toute seule (micro débranché, erreur pilote...).</summary>
    public event Action<Exception>? Faulted;

    public bool IsRunning { get; private set; }

    /// <summary>Format natif du microphone (information).</summary>
    public WaveFormat? DeviceFormat { get; private set; }

    /// <summary>Nombre de coupures audio : le micro n'a pas fourni ses données à temps.</summary>
    public long Dropouts => _pipeline?.Underruns ?? 0;

    /// <summary>Démarre la capture et retourne la source 48 kHz stéréo à brancher sur le mixeur.</summary>
    public ISampleProvider Start(MMDevice device, int bufferMilliseconds = 20)
    {
        lock (_gate)
        {
            StopCore();

            var capture = new WasapiCapture(device, true, bufferMilliseconds);
            try
            {
                var format = capture.WaveFormat;
                _sampleKind = SampleConverter.GetSampleKind(format);
                _pipeline = new CaptureSourcePipeline(format.SampleRate, format.Channels);
                DeviceFormat = format;

                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnRecordingStopped;
                capture.StartRecording();
            }
            catch
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                capture.Dispose();
                _pipeline = null;
                throw;
            }

            _capture = capture;
            _device = device;
            IsRunning = true;
            return _pipeline.Output;
        }
    }

    public void Stop()
    {
        lock (_gate) StopCore();
    }

    private void StopCore()
    {
        var capture = _capture;
        _capture = null;
        IsRunning = false;
        if (capture != null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try { capture.Dispose(); } catch { /* périphérique déjà retiré */ }
        }
        _device?.Dispose();
        _device = null;
        _pipeline = null;
    }

    // Thread de capture : pas d'allocation, pas de verrou.
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _pipeline?.Write(e.Buffer.AsSpan(0, e.BytesRecorded), _sampleKind);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (!IsRunning) return;
        IsRunning = false;
        Faulted?.Invoke(e.Exception ?? new InvalidOperationException("La capture du microphone s'est arrêtée."));
    }

    public void Dispose() => Stop();
}

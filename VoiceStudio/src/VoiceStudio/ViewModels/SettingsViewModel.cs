using System.Collections.ObjectModel;
using VoiceStudio.Audio;
using VoiceStudio.Audio.Devices;
using VoiceStudio.Helpers;
using VoiceStudio.Settings;

namespace VoiceStudio.ViewModels;

/// <summary>Écran « Réglages » : latence, applications exclues, périphérique de retour.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AudioEngine _engine;
    private readonly AppSettings _settings;
    private readonly Action _restartIfRunning;
    private readonly Action _applyMonitor;
    private string _excludedAppsText;
    private AudioDeviceInfo? _selectedMonitor;

    public SettingsViewModel(AudioEngine engine, AppSettings settings,
        Action restartIfRunning, Action applyMonitor)
    {
        _engine = engine;
        _settings = settings;
        _restartIfRunning = restartIfRunning;
        _applyMonitor = applyMonitor;
        _excludedAppsText = string.Join(", ", settings.ExcludedApps);
        engine.SystemAudio.ExcludedApps = settings.ExcludedApps;
        RefreshMonitors();
    }

    public ObservableCollection<AudioDeviceInfo> MonitorDevices { get; } = [];

    /// <summary>Casque ou enceintes où s'écouter soi-même.</summary>
    public AudioDeviceInfo? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (!SetProperty(ref _selectedMonitor, value) || value == null) return;
            _settings.MonitorId = value.Id;
            _applyMonitor();
        }
    }

    public int OutputLatencyMs
    {
        get => _settings.OutputLatencyMs;
        set
        {
            int clamped = Math.Clamp(value, 10, 200);
            if (_settings.OutputLatencyMs == clamped) return;
            _settings.OutputLatencyMs = clamped;
            OnPropertyChanged();
            _restartIfRunning();
        }
    }

    /// <summary>Applications exclues de la capture, séparées par des virgules.</summary>
    public string ExcludedAppsText
    {
        get => _excludedAppsText;
        set
        {
            if (!SetProperty(ref _excludedAppsText, value)) return;
            _settings.ExcludedApps = value
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            _engine.SystemAudio.ExcludedApps = _settings.ExcludedApps;
        }
    }

    public void RefreshMonitors()
    {
        var outputs = _engine.Devices.GetOutputs().Where(device => !device.IsVirtualCable).ToList();
        if (!MonitorDevices.SequenceEqual(outputs))
        {
            MonitorDevices.Clear();
            foreach (var device in outputs) MonitorDevices.Add(device);
        }

        _selectedMonitor = outputs.FirstOrDefault(device => device.Id == _settings.MonitorId)
                           ?? outputs.FirstOrDefault(device => device.IsDefault)
                           ?? outputs.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedMonitor));
    }
}

using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly SettingsService _service;
    private readonly IFilePicker _filePicker;
    private readonly Action _settingsReloaded;
    private string? _message;
    private string _excludedAppsText;
    private bool _isCapturingOverlayKey;
    private AudioDeviceInfo? _selectedMonitor;

    public SettingsViewModel(AudioEngine engine, AppSettings settings, SettingsService service,
        IFilePicker filePicker, Action restartIfRunning, Action applyMonitor, Action settingsReloaded)
    {
        _service = service;
        _filePicker = filePicker;
        _settingsReloaded = settingsReloaded;
        _engine = engine;
        _settings = settings;
        _restartIfRunning = restartIfRunning;
        _applyMonitor = applyMonitor;
        _excludedAppsText = string.Join(", ", settings.ExcludedApps);
        OpenJournalCommand = new RelayCommand(OpenJournal);
        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
        CaptureOverlayKeyCommand = new RelayCommand(() => IsCapturingOverlayKey = true);
        ClearOverlayKeyCommand = new RelayCommand(() => SetOverlayKey(0, ""));
        engine.SystemAudio.ExcludedApps = settings.ExcludedApps;
        RefreshMonitors();
    }

    public ObservableCollection<AudioDeviceInfo> MonitorDevices { get; } = [];

    public RelayCommand OpenJournalCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand CaptureOverlayKeyCommand { get; }

    /// <summary>Latence totale annoncée entre ta voix et Discord.</summary>
    public string LatencyText => _engine.IsRunning
        ? $"Environ {_engine.EstimatedLatencyMs} ms entre ta voix et Discord."
        : "Démarre Voice Studio pour connaître la latence réelle.";

    public void RefreshLatency() => OnPropertyChanged(nameof(LatencyText));

    public string SettingsFilePath => _service.FilePath;

    public string? Message
    {
        get => _message;
        private set
        {
            if (!SetProperty(ref _message, value)) return;
            OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    private void OpenJournal()
    {
        try
        {
            if (!System.IO.File.Exists(Journal.Path))
                Journal.Write("Journal ouvert depuis les réglages");
            Process.Start(new ProcessStartInfo(Journal.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Message = "Impossible d'ouvrir le journal : " + ex.Message;
        }
    }

    private void Export()
    {
        string? path = _filePicker.PickSettingsFileToSave("voice-studio-reglages.json");
        if (path == null) return;
        Message = _service.Export(_settings, path)
            ? $"Réglages enregistrés dans {path}."
            : "L'enregistrement a échoué, le journal en dit plus.";
    }

    private void Import()
    {
        string? path = _filePicker.PickSettingsFileToOpen();
        if (path == null) return;
        var imported = _service.Import(path);
        if (imported == null)
        {
            Message = "Ce fichier n'a pas pu être lu.";
            return;
        }
        _service.Save(imported);
        Message = "Réglages restaurés. Redémarre Voice Studio pour qu'ils s'appliquent partout.";
        _settingsReloaded();
    }
    public RelayCommand ClearOverlayKeyCommand { get; }

    // --- Panneau flottant ---

    /// <summary>Opacité du panneau, de 20 à 100 %.</summary>
    public double OverlayOpacity
    {
        get => _settings.OverlayOpacity;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 20, 100);
            if (Math.Abs(_settings.OverlayOpacity - clamped) < 0.5) return;
            _settings.OverlayOpacity = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverlayOpacityText));
            OverlayAppearanceChanged?.Invoke();
        }
    }

    public string OverlayOpacityText => $"{OverlayOpacity:0} %";

    public bool OverlayTopmost
    {
        get => _settings.OverlayTopmost;
        set
        {
            _settings.OverlayTopmost = value;
            OnPropertyChanged();
            OverlayAppearanceChanged?.Invoke();
        }
    }

    public string OverlayKeyText => _settings.OverlayKeyName.Length > 0
        ? _settings.OverlayKeyName
        : "Aucune touche";

    public bool IsCapturingOverlayKey
    {
        get => _isCapturingOverlayKey;
        set => SetProperty(ref _isCapturingOverlayKey, value);
    }

    public void SetOverlayKey(int virtualKey, string name)
    {
        _settings.OverlayVirtualKey = virtualKey;
        _settings.OverlayKeyName = name;
        IsCapturingOverlayKey = false;
        OnPropertyChanged(nameof(OverlayKeyText));
        HotkeysChanged?.Invoke();
    }

    /// <summary>Levé quand l'opacité ou le premier plan changent.</summary>
    public event Action? OverlayAppearanceChanged;

    /// <summary>Levé quand la touche du panneau change.</summary>
    public event Action? HotkeysChanged;

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

    /// <summary>
    /// Mode compatibilité : à essayer en premier contre les craquements, surtout avec
    /// un micro ou un câble virtuels.
    /// </summary>
    public bool CompatibilityMode
    {
        get => _settings.CompatibilityMode;
        set
        {
            if (_settings.CompatibilityMode == value) return;
            _settings.CompatibilityMode = value;
            OnPropertyChanged();
            _restartIfRunning();
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

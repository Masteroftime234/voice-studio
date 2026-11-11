using System.Collections.ObjectModel;
using VoiceStudio.Audio;
using VoiceStudio.Audio.Devices;
using VoiceStudio.Audio.Services;
using VoiceStudio.Helpers;
using VoiceStudio.Settings;

namespace VoiceStudio.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AudioEngine _engine;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly SynchronizationContext? _uiContext;

    private AudioDeviceInfo? _selectedMicrophone;
    private AudioDeviceInfo? _selectedOutput;
    private bool _isRunning;
    private string? _errorMessage;
    private string _systemAudioStatus = "Arrêté";
    private string _outputStatus = "Arrêté";
    private bool _isOutputFlowing;
    private bool _cableMissing;
    private double _masterLevel;
    private bool _isLimiting;
    private string _excludedAppsText;
    private long _lastFramesRendered;
    private int _stalledTicks;

    public MainViewModel(AudioEngine engine, SettingsService settingsService)
    {
        _engine = engine;
        _settingsService = settingsService;
        _settings = settingsService.Load();
        _uiContext = SynchronizationContext.Current;

        Microphone = new ChannelStripViewModel(engine.Mixer.Microphone, "Microphone", "Ta voix")
        {
            Volume = _settings.MicrophoneVolume,
            IsMuted = _settings.MicrophoneMuted
        };
        SystemAudio = new ChannelStripViewModel(engine.Mixer.SystemAudio, "Sons du PC",
            "Tout ce que tu écoutes (musique, vidéos, jeux…)")
        {
            Volume = _settings.SystemAudioVolume,
            IsMuted = _settings.SystemAudioMuted
        };
        engine.Mixer.Engine.MasterVolume = (float)(_settings.MasterVolume / 100.0);

        _excludedAppsText = string.Join(", ", _settings.ExcludedApps);
        engine.SystemAudio.ExcludedApps = _settings.ExcludedApps;

        ToggleEngineCommand = new RelayCommand(ToggleEngine);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);

        engine.StateChanged += () => RunOnUi(OnEngineStateChanged);
        engine.SystemAudio.StateChanged += () => RunOnUi(() => SystemAudioStatus = engine.SystemAudio.StatusMessage);
        engine.Devices.DevicesChanged += () => RunOnUi(RefreshDevices);

        RefreshDevices();
    }

    public ObservableCollection<AudioDeviceInfo> Microphones { get; } = [];
    public ObservableCollection<AudioDeviceInfo> Outputs { get; } = [];

    public ChannelStripViewModel Microphone { get; }
    public ChannelStripViewModel SystemAudio { get; }

    public RelayCommand ToggleEngineCommand { get; }
    public RelayCommand RefreshDevicesCommand { get; }

    public AudioDeviceInfo? SelectedMicrophone
    {
        get => _selectedMicrophone;
        set
        {
            if (!SetProperty(ref _selectedMicrophone, value) || value == null) return;
            _settings.MicrophoneId = value.Id;
            RestartIfRunning();
        }
    }

    public AudioDeviceInfo? SelectedOutput
    {
        get => _selectedOutput;
        set
        {
            if (!SetProperty(ref _selectedOutput, value) || value == null) return;
            _settings.OutputId = value.Id;
            RestartIfRunning();
        }
    }

    public double MasterVolume
    {
        get => Math.Round(_engine.Mixer.Engine.MasterVolume * 100.0);
        set
        {
            _engine.Mixer.Engine.MasterVolume = (float)(value / 100.0);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MasterVolumeText));
        }
    }

    public string MasterVolumeText => $"{MasterVolume:0} %";

    public double MasterLevel
    {
        get => _masterLevel;
        private set => SetProperty(ref _masterLevel, value);
    }

    /// <summary>Le limiteur est en train de réduire le volume pour éviter la saturation.</summary>
    public bool IsLimiting
    {
        get => _isLimiting;
        private set => SetProperty(ref _isLimiting, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value)) return;
            OnPropertyChanged(nameof(ToggleLabel));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string ToggleLabel => IsRunning ? "Arrêter" : "Démarrer";

    /// <summary>Phrase d'état affichée sous le titre.</summary>
    public string StatusText => !IsRunning
        ? "Arrêté — clique sur Démarrer"
        : IsOutputFlowing
            ? "En marche — ta voix et tes sons partent vers Discord"
            : "Démarrage…";

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value)) return;
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string SystemAudioStatus
    {
        get => _systemAudioStatus;
        private set => SetProperty(ref _systemAudioStatus, value);
    }

    public string OutputStatus
    {
        get => _outputStatus;
        private set => SetProperty(ref _outputStatus, value);
    }

    /// <summary>Le flux audio arrive réellement dans le micro virtuel.</summary>
    public bool IsOutputFlowing
    {
        get => _isOutputFlowing;
        private set
        {
            if (SetProperty(ref _isOutputFlowing, value)) OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>Aucun câble virtuel (VB-CABLE) n'a été détecté.</summary>
    public bool CableMissing
    {
        get => _cableMissing;
        private set => SetProperty(ref _cableMissing, value);
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
            RestartIfRunning();
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

    /// <summary>Appelé ~30 fois par seconde par la fenêtre pour animer les indicateurs.</summary>
    public void RefreshMeters()
    {
        Microphone.RefreshLevel();
        SystemAudio.RefreshLevel();
        var mixer = _engine.Mixer.Engine;
        MasterLevel = MeterScale.ToMeter(mixer.Peak);
        IsLimiting = mixer.LimiterGain < 0.98f;

        if (!IsRunning) return;
        long frames = mixer.FramesRendered;
        _stalledTicks = frames != _lastFramesRendered ? 0 : _stalledTicks + 1;
        _lastFramesRendered = frames;
        bool flowing = _stalledTicks < 15; // ~0,5 s sans nouvelle trame = flux bloqué
        IsOutputFlowing = flowing;
        OutputStatus = flowing ? "Actif — le flux arrive dans le micro virtuel" : "Flux bloqué";
    }

    public void RefreshDevices()
    {
        var microphones = _engine.Devices.GetMicrophones()
            // « CABLE Output » est l'autre bout du câble virtuel : ce n'est pas un vrai micro.
            .Where(device => !device.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var outputs = _engine.Devices.GetOutputs();

        Replace(Microphones, microphones);
        Replace(Outputs, outputs);
        CableMissing = !outputs.Any(device => device.IsVirtualCable);

        _selectedMicrophone = microphones.FirstOrDefault(device => device.Id == _settings.MicrophoneId)
                              ?? microphones.FirstOrDefault(device => device.IsDefault)
                              ?? microphones.FirstOrDefault();
        // Sortie : uniquement le choix enregistré ou un câble virtuel détecté.
        // On ne choisit jamais tout seul des enceintes : la voix y serait rejouée.
        _selectedOutput = outputs.FirstOrDefault(device => device.Id == _settings.OutputId)
                          ?? outputs.FirstOrDefault(device => device.IsVirtualCable);
        OnPropertyChanged(nameof(SelectedMicrophone));
        OnPropertyChanged(nameof(SelectedOutput));
    }

    public void SaveSettings()
    {
        _settings.MicrophoneVolume = Microphone.Volume;
        _settings.MicrophoneMuted = Microphone.IsMuted;
        _settings.SystemAudioVolume = SystemAudio.Volume;
        _settings.SystemAudioMuted = SystemAudio.IsMuted;
        _settings.MasterVolume = MasterVolume;
        _settingsService.Save(_settings);
    }

    private void ToggleEngine()
    {
        if (_engine.IsRunning) _engine.Stop();
        else StartEngine();
    }

    private void StartEngine()
    {
        if (SelectedMicrophone == null)
        {
            ErrorMessage = "Aucun microphone sélectionné.";
            return;
        }
        if (SelectedOutput == null)
        {
            ErrorMessage = CableMissing
                ? "Aucun micro virtuel détecté. Installe VB-CABLE (vb-audio.com/Cable), redémarre le PC, puis clique sur Actualiser."
                : "Aucune sortie virtuelle sélectionnée.";
            return;
        }

        try
        {
            _stalledTicks = 0;
            _engine.Start(SelectedMicrophone.Id, SelectedOutput.Id, _settings.OutputLatencyMs);
            SaveSettings();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            OnEngineStateChanged();
        }
    }

    private void RestartIfRunning()
    {
        if (!_engine.IsRunning) return;
        _engine.Stop();
        StartEngine();
    }

    private void OnEngineStateChanged()
    {
        IsRunning = _engine.IsRunning;
        if (_engine.LastError != null) ErrorMessage = _engine.LastError;
        else if (IsRunning) ErrorMessage = null;

        if (!IsRunning)
        {
            IsOutputFlowing = false;
            OutputStatus = "Arrêté";
            SystemAudioStatus = "Arrêté";
        }
    }

    private void RunOnUi(Action action)
    {
        if (_uiContext == null || SynchronizationContext.Current == _uiContext) action();
        else _uiContext.Post(_ => action(), null);
    }

    private static void Replace(ObservableCollection<AudioDeviceInfo> target, IReadOnlyList<AudioDeviceInfo> items)
    {
        if (target.SequenceEqual(items)) return;
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}

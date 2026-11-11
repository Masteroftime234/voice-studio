using VoiceStudio.Audio;
using VoiceStudio.Audio.Services;
using VoiceStudio.Helpers;
using VoiceStudio.Settings;

namespace VoiceStudio.ViewModels;

/// <summary>
/// Fenêtre principale : navigation entre les pages, démarrage et arrêt du moteur,
/// et distribution des raccourcis clavier.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AudioEngine _engine;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly SynchronizationContext? _uiContext;
    private readonly GlobalKeyWatcher _keys = new();

    private string _section = "mixer";
    private bool _isRunning;
    private string? _errorMessage;
    private string _outputStatus = "Arrêté";
    private bool _isOutputFlowing;
    private long _lastFramesRendered;
    private int _stalledTicks;

    public MainViewModel(AudioEngine engine, SettingsService settingsService, IFilePicker filePicker)
    {
        _engine = engine;
        _settingsService = settingsService;
        _settings = settingsService.Load();
        _uiContext = SynchronizationContext.Current;

        Mixer = new MixerViewModel(engine, _settings, RestartIfRunning);
        VoiceFx = new VoiceFxViewModel(engine.VoiceEffects, _settings);
        Microphone = new MicrophoneViewModel(engine, _settings, ApplyMonitor, RefreshHotkeys);
        Soundboard = new SoundboardViewModel(engine.Soundboard, _settings, filePicker, RefreshHotkeys);
        SettingsPage = new SettingsViewModel(engine, _settings, RestartIfRunning, ApplyMonitor);

        engine.SystemAudio.ResolveSavedLevels = Mixer.SavedLevelsFor;

        ToggleEngineCommand = new RelayCommand(ToggleEngine);
        GoToCommand = new RelayCommand<string>(section => Section = section);

        engine.StateChanged += () => RunOnUi(OnEngineStateChanged);
        engine.SystemAudio.StateChanged += () => RunOnUi(() => Mixer.SystemAudioStatus = engine.SystemAudio.StatusMessage);
        engine.SystemAudio.SourcesChanged += () => RunOnUi(Mixer.SyncApps);
        engine.Devices.DevicesChanged += () => RunOnUi(() =>
        {
            Mixer.RefreshDevices();
            SettingsPage.RefreshMonitors();
        });

        _keys.KeyStateChanged += OnKeyStateChanged;
        RefreshHotkeys();
    }

    public MixerViewModel Mixer { get; }
    public VoiceFxViewModel VoiceFx { get; }
    public MicrophoneViewModel Microphone { get; }
    public SoundboardViewModel Soundboard { get; }
    public SettingsViewModel SettingsPage { get; }

    public RelayCommand ToggleEngineCommand { get; }
    public RelayCommand<string> GoToCommand { get; }

    /// <summary>Page affichée : mixer, voix, micro, soundboard ou reglages.</summary>
    public string Section
    {
        get => _section;
        set => SetProperty(ref _section, value);
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

    /// <summary>Appelé ~30 fois par seconde par la fenêtre pour animer les indicateurs.</summary>
    public void RefreshMeters()
    {
        Mixer.RefreshMeters();
        Soundboard.RefreshPlayingState();

        if (!IsRunning) return;
        long frames = _engine.Mixer.Engine.FramesRendered;
        _stalledTicks = frames != _lastFramesRendered ? 0 : _stalledTicks + 1;
        _lastFramesRendered = frames;
        bool flowing = _stalledTicks < 15; // ~0,5 s sans nouvelle trame = flux bloqué
        IsOutputFlowing = flowing;
        OutputStatus = flowing ? "Actif — le flux arrive dans le micro virtuel" : "Flux bloqué";
    }

    public void SaveSettings()
    {
        Mixer.CaptureSettings();
        _settingsService.Save(_settings);
    }

    public void Dispose()
    {
        _keys.KeyStateChanged -= OnKeyStateChanged;
        _keys.Dispose();
    }

    // ---- Raccourcis clavier ----

    private void RefreshHotkeys()
    {
        var keys = new List<int>(Soundboard.HotkeyVirtualKeys);
        if (_settings.PushToTalkEnabled && _settings.PushToTalkVirtualKey != 0)
            keys.Add(_settings.PushToTalkVirtualKey);
        _keys.WatchOnly(keys);
    }

    private void OnKeyStateChanged(int virtualKey, bool down)
    {
        if (_settings.PushToTalkEnabled && virtualKey == _settings.PushToTalkVirtualKey)
            _engine.PushToTalkPressed = down;

        if (!down) return;
        var sound = Soundboard.FindByHotkey(virtualKey);
        if (sound != null) RunOnUi(() => _ = Soundboard.PlayAsync(sound));
    }

    // ---- Moteur ----

    private void ToggleEngine()
    {
        if (_engine.IsRunning) _engine.Stop();
        else StartEngine();
    }

    private void StartEngine()
    {
        if (Mixer.SelectedMicrophone == null)
        {
            ErrorMessage = "Aucun microphone sélectionné.";
            return;
        }
        if (Mixer.SelectedOutput == null)
        {
            ErrorMessage = Mixer.CableMissing
                ? "Aucun micro virtuel détecté. Installe VB-CABLE (vb-audio.com/Cable), redémarre le PC, puis clique sur Actualiser."
                : "Aucune sortie virtuelle sélectionnée.";
            return;
        }

        try
        {
            _stalledTicks = 0;
            _engine.Start(new EngineOptions(Mixer.SelectedMicrophone.Id, Mixer.SelectedOutput.Id)
            {
                OutputLatencyMs = _settings.OutputLatencyMs,
                MonitorId = _settings.MonitorEnabled ? SettingsPage.SelectedMonitor?.Id : null
            });
            SaveSettings();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            OnEngineStateChanged();
        }
    }

    private void ApplyMonitor() =>
        _engine.SetMonitor(
            Microphone.MonitorEnabled ? SettingsPage.SelectedMonitor?.Id : null,
            _settings.OutputLatencyMs);

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
        else if (_engine.LastWarning != null) ErrorMessage = _engine.LastWarning;
        else if (IsRunning) ErrorMessage = null;

        if (!IsRunning)
        {
            IsOutputFlowing = false;
            OutputStatus = "Arrêté";
            Mixer.SystemAudioStatus = "Arrêté";
        }
    }

    private void RunOnUi(Action action)
    {
        if (_uiContext == null || SynchronizationContext.Current == _uiContext) action();
        else _uiContext.Post(_ => action(), null);
    }
}

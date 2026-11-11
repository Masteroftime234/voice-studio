using System.Collections.ObjectModel;
using VoiceStudio.Audio;
using VoiceStudio.Audio.Devices;
using VoiceStudio.Helpers;
using VoiceStudio.Settings;

namespace VoiceStudio.ViewModels;

/// <summary>Écran « Mixer » : les périphériques et une tranche par source de son.</summary>
public sealed class MixerViewModel : ObservableObject
{
    private readonly AudioEngine _engine;
    private readonly AppSettings _settings;
    private readonly Action _restartIfRunning;

    private AudioDeviceInfo? _selectedMicrophone;
    private AudioDeviceInfo? _selectedOutput;
    private bool _cableMissing;
    private double _masterLevel;
    private double _appsLevel;
    private bool _isLimiting;
    private string _systemAudioStatus = "Arrêté";

    public MixerViewModel(AudioEngine engine, AppSettings settings, Action restartIfRunning)
    {
        _engine = engine;
        _settings = settings;
        _restartIfRunning = restartIfRunning;

        Microphone = new ChannelViewModel(engine.Mixer.Microphone, "Micro", "Ta voix")
        {
            Volume = settings.MicrophoneVolume,
            IsMuted = settings.MicrophoneMuted
        };
        Soundboard = new ChannelViewModel(engine.Mixer.Soundboard, "Soundboard", "Tes extraits")
        {
            Volume = settings.SoundboardVolume,
            IsMuted = settings.SoundboardMuted
        };

        string[] accents = ["BusGameBrush", "BusChatBrush", "BusMediaBrush", "BusOtherBrush"];
        for (int i = 0; i < engine.Mixer.Buses.Count; i++)
        {
            var bus = new BusViewModel(engine.Mixer.Buses[i], accents[i % accents.Length]);
            if (settings.BusLevels.TryGetValue(bus.Id, out var level))
            {
                bus.Volume = level.Volume;
                bus.IsMuted = level.Muted;
            }
            else
            {
                // Au premier lancement, les sons du PC passent sous la voix.
                bus.Volume = 70;
            }
            Buses.Add(bus);
        }
        engine.Mixer.Engine.MasterVolume = (float)(settings.MasterVolume / 100.0);

        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        RefreshDevices();
    }

    public ChannelViewModel Microphone { get; }
    public ChannelViewModel Soundboard { get; }

    /// <summary>Les bus : Jeu, Chat, Média, Autres.</summary>
    public ObservableCollection<BusViewModel> Buses { get; } = [];

    /// <summary>
    /// Applications qui jouent du son mais restent hors du mix, comme Discord :
    /// les capturer renverrait à tes amis leur propre voix.
    /// </summary>
    public ObservableCollection<string> ExcludedPlaying { get; } = [];

    public bool HasExcludedPlaying => ExcludedPlaying.Count > 0;

    public ObservableCollection<AudioDeviceInfo> Microphones { get; } = [];
    public ObservableCollection<AudioDeviceInfo> Outputs { get; } = [];

    public RelayCommand RefreshDevicesCommand { get; }

    public AudioDeviceInfo? SelectedMicrophone
    {
        get => _selectedMicrophone;
        set
        {
            if (!SetProperty(ref _selectedMicrophone, value) || value == null) return;
            _settings.MicrophoneId = value.Id;
            _restartIfRunning();
        }
    }

    public AudioDeviceInfo? SelectedOutput
    {
        get => _selectedOutput;
        set
        {
            if (!SetProperty(ref _selectedOutput, value) || value == null) return;
            _settings.OutputId = value.Id;
            _restartIfRunning();
        }
    }

    /// <summary>Aucun câble virtuel (VB-CABLE) n'a été détecté.</summary>
    public bool CableMissing
    {
        get => _cableMissing;
        private set => SetProperty(ref _cableMissing, value);
    }

    /// <summary>
    /// Volume de tous les sons du PC à la fois, pour la page Simple :
    /// déplace les quatre bus ensemble.
    /// </summary>
    public double AppsVolume
    {
        get => Buses.Count == 0 ? 100 : Math.Round(Buses.Average(bus => bus.Volume));
        set
        {
            foreach (var bus in Buses) bus.Volume = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AppsVolumeText));
        }
    }

    public string AppsVolumeText => $"{AppsVolume:0} %";

    /// <summary>Niveau le plus fort parmi les bus, pour l'indicateur de la page Simple.</summary>
    public double AppsLevel
    {
        get => _appsLevel;
        private set => SetProperty(ref _appsLevel, value);
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

    /// <summary>Le limiteur réduit le volume pour éviter la saturation.</summary>
    public bool IsLimiting
    {
        get => _isLimiting;
        private set => SetProperty(ref _isLimiting, value);
    }

    public string SystemAudioStatus
    {
        get => _systemAudioStatus;
        set => SetProperty(ref _systemAudioStatus, value);
    }

    /// <summary>Volume et état enregistrés pour une application (appelé depuis le moteur).</summary>
    public (float Volume, bool Muted) SavedLevelsFor(string appName)
    {
        lock (_settings.AppLevels)
        {
            return _settings.AppLevels.TryGetValue(appName, out var level)
                ? ((float)(level.Volume / 100.0), level.Muted)
                : (1f, false);
        }
    }

    /// <summary>Bus enregistré pour une application (appelé depuis le moteur).</summary>
    public string? SavedBusFor(string appName)
    {
        lock (_settings.AppBuses)
        {
            return _settings.AppBuses.TryGetValue(appName, out string? busId) ? busId : null;
        }
    }

    /// <summary>Range les applications captées dans leurs bus.</summary>
    public void SyncApps()
    {
        var sources = _engine.SystemAudio.Sources;
        var existing = Buses.SelectMany(bus => bus.Apps)
            .Where(app => app.AppName != null)
            .ToDictionary(app => app.AppName!, StringComparer.OrdinalIgnoreCase);

        foreach (var bus in Buses)
        {
            var apps = sources
                .Where(source => _engine.BusOf(source.Name) == bus.Id)
                .Select(source => existing.TryGetValue(source.Name, out var known) ? known
                                  : new ChannelViewModel(source))
                .ToList();
            if (!bus.Apps.SequenceEqual(apps)) bus.SetApps(apps);
        }
        OnPropertyChanged(nameof(HasApps));

        var excluded = _engine.SystemAudio.ExcludedPlaying
            .Select(FriendlyAppName.For)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!ExcludedPlaying.SequenceEqual(excluded))
        {
            ExcludedPlaying.Clear();
            foreach (string name in excluded) ExcludedPlaying.Add(name);
            OnPropertyChanged(nameof(HasExcludedPlaying));
        }
    }

    /// <summary>Déplace une application vers un autre bus (glisser-déposer).</summary>
    public void MoveApp(ChannelViewModel app, BusViewModel target)
    {
        if (app.AppName == null) return;
        lock (_settings.AppBuses) _settings.AppBuses[app.AppName] = target.Id;
        _engine.AssignApp(app.AppName, target.Id);
        SyncApps();
    }

    public bool HasApps => Buses.Any(bus => bus.Apps.Count > 0);

    public void RefreshMeters()
    {
        Microphone.RefreshLevel();
        Soundboard.RefreshLevel();
        foreach (var bus in Buses) bus.RefreshLevel();

        AppsLevel = Buses.Count == 0 ? 0 : Buses.Max(bus => bus.Level);

        var mixer = _engine.Mixer.Engine;
        MasterLevel = MeterScale.ToMeter(mixer.Peak);
        IsLimiting = mixer.LimiterGain < 0.98f;
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

    /// <summary>Recopie les volumes dans les réglages avant enregistrement.</summary>
    public void CaptureSettings()
    {
        _settings.MicrophoneVolume = Microphone.Volume;
        _settings.MicrophoneMuted = Microphone.IsMuted;
        _settings.SoundboardVolume = Soundboard.Volume;
        _settings.SoundboardMuted = Soundboard.IsMuted;
        _settings.MasterVolume = MasterVolume;

        lock (_settings.AppLevels)
        {
            foreach (var app in Buses.SelectMany(bus => bus.Apps))
            {
                if (app.AppName == null) continue;
                _settings.AppLevels[app.AppName] = new AppLevel { Volume = app.Volume, Muted = app.IsMuted };
            }
        }

        foreach (var bus in Buses)
            _settings.BusLevels[bus.Id] = new AppLevel { Volume = bus.Volume, Muted = bus.IsMuted };
    }

    private static void Replace(ObservableCollection<AudioDeviceInfo> target, IReadOnlyList<AudioDeviceInfo> items)
    {
        if (target.SequenceEqual(items)) return;
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}

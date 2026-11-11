using VoiceStudio.Audio;
using VoiceStudio.Helpers;
using VoiceStudio.Settings;

namespace VoiceStudio.ViewModels;

/// <summary>Écran « Micro » : nettoyage du son, push-to-talk, retour audio, auto-ducking.</summary>
public sealed class MicrophoneViewModel : ObservableObject
{
    private readonly AudioEngine _engine;
    private readonly AppSettings _settings;
    private readonly Action _applyMonitor;
    private readonly Action _hotkeysChanged;
    private bool _isCapturingKey;
    private long _dropouts;

    public MicrophoneViewModel(AudioEngine engine, AppSettings settings, Action applyMonitor, Action hotkeysChanged)
    {
        _engine = engine;
        _settings = settings;
        _applyMonitor = applyMonitor;
        _hotkeysChanged = hotkeysChanged;

        var suppressor = engine.VoiceEffects.Chain.NoiseSuppression;
        suppressor.Enabled = settings.NoiseSuppressionEnabled;
        suppressor.Strength = (float)(settings.NoiseSuppressionStrength / 100.0);

        var gate = engine.VoiceEffects.Chain.Gate;
        gate.Enabled = settings.NoiseGateEnabled;
        gate.ThresholdDb = (float)settings.NoiseGateThresholdDb;

        engine.PushToTalkEnabled = settings.PushToTalkEnabled;
        engine.Monitor.Volume = (float)(settings.MonitorVolume / 100.0);

        var ducker = engine.Ducker;
        ducker.Enabled = settings.DuckingEnabled;
        ducker.ThresholdDb = (float)settings.DuckingThresholdDb;
        ducker.MinimumVolume = (float)(settings.DuckingMinimumVolume / 100.0);
        ducker.AttackMs = (float)settings.DuckingAttackMs;
        ducker.ReleaseMs = (float)settings.DuckingReleaseMs;

        CaptureKeyCommand = new RelayCommand(() => IsCapturingKey = true);
        ClearKeyCommand = new RelayCommand(() => SetPushToTalkKey(0, ""));
    }

    public RelayCommand CaptureKeyCommand { get; }

    /// <summary>Compteur de coupures, rafraîchi par la fenêtre.</summary>
    public long Dropouts
    {
        get => _dropouts;
        private set
        {
            if (!SetProperty(ref _dropouts, value)) return;
            OnPropertyChanged(nameof(DropoutText));
            OnPropertyChanged(nameof(HasDropouts));
        }
    }

    public bool HasDropouts => Dropouts > 0;

    public string DropoutText => Dropouts == 0
        ? "Aucune coupure audio."
        : $"{Dropouts} coupure(s) audio depuis le démarrage. Si le nombre monte pendant que tu parles, "
          + "augmente la latence de sortie dans Réglages.";

    public void RefreshDropouts() => Dropouts = _engine.Dropouts;
    public RelayCommand ClearKeyCommand { get; }

    // --- Suppression du bruit ---

    public bool NoiseSuppressionEnabled
    {
        get => _engine.VoiceEffects.Chain.NoiseSuppression.Enabled;
        set
        {
            _engine.VoiceEffects.Chain.NoiseSuppression.Enabled = value;
            _settings.NoiseSuppressionEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Intensité de la suppression, en pourcentage.</summary>
    public double NoiseSuppressionStrength
    {
        get => Math.Round(_engine.VoiceEffects.Chain.NoiseSuppression.Strength * 100.0);
        set
        {
            _engine.VoiceEffects.Chain.NoiseSuppression.Strength = (float)(value / 100.0);
            _settings.NoiseSuppressionStrength = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NoiseSuppressionStrengthText));
        }
    }

    public string NoiseSuppressionStrengthText => NoiseSuppressionStrength switch
    {
        < 35 => "Légère — ne touche presque pas à la voix",
        < 75 => "Équilibrée — recommandée",
        _ => "Maximale — la voix peut devenir un peu sourde"
    };

    // --- Noise gate ---

    public bool NoiseGateEnabled
    {
        get => _engine.VoiceEffects.Chain.Gate.Enabled;
        set
        {
            _engine.VoiceEffects.Chain.Gate.Enabled = value;
            _settings.NoiseGateEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Seuil d'ouverture du micro, en dB.</summary>
    public double NoiseGateThresholdDb
    {
        get => _engine.VoiceEffects.Chain.Gate.ThresholdDb;
        set
        {
            _engine.VoiceEffects.Chain.Gate.ThresholdDb = (float)value;
            _settings.NoiseGateThresholdDb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NoiseGateThresholdText));
        }
    }

    public string NoiseGateThresholdText => $"{NoiseGateThresholdDb:0} dB";

    // --- Push-to-talk ---

    public bool PushToTalkEnabled
    {
        get => _engine.PushToTalkEnabled;
        set
        {
            _engine.PushToTalkEnabled = value;
            _settings.PushToTalkEnabled = value;
            OnPropertyChanged();
            _hotkeysChanged();
        }
    }

    public int PushToTalkVirtualKey => _settings.PushToTalkVirtualKey;

    public string PushToTalkKeyText => _settings.PushToTalkName.Length > 0
        ? _settings.PushToTalkName
        : "Aucune touche";

    /// <summary>L'interface attend que l'utilisateur appuie sur une touche.</summary>
    public bool IsCapturingKey
    {
        get => _isCapturingKey;
        set => SetProperty(ref _isCapturingKey, value);
    }

    public void SetPushToTalkKey(int virtualKey, string name)
    {
        _settings.PushToTalkVirtualKey = virtualKey;
        _settings.PushToTalkName = name;
        IsCapturingKey = false;
        OnPropertyChanged(nameof(PushToTalkKeyText));
        _hotkeysChanged();
    }

    // --- Retour audio ---

    public bool MonitorEnabled
    {
        get => _settings.MonitorEnabled;
        set
        {
            _settings.MonitorEnabled = value;
            OnPropertyChanged();
            _applyMonitor();
        }
    }

    public double MonitorVolume
    {
        get => Math.Round(_engine.Monitor.Volume * 100.0);
        set
        {
            _engine.Monitor.Volume = (float)(value / 100.0);
            _settings.MonitorVolume = value;
            OnPropertyChanged();
        }
    }

    // --- Auto-ducking ---

    public bool DuckingEnabled
    {
        get => _engine.Ducker.Enabled;
        set
        {
            _engine.Ducker.Enabled = value;
            _settings.DuckingEnabled = value;
            OnPropertyChanged();
        }
    }

    public double DuckingMinimumVolume
    {
        get => Math.Round(_engine.Ducker.MinimumVolume * 100.0);
        set
        {
            _engine.Ducker.MinimumVolume = (float)(value / 100.0);
            _settings.DuckingMinimumVolume = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DuckingMinimumVolumeText));
        }
    }

    public string DuckingMinimumVolumeText => $"{DuckingMinimumVolume:0} %";

    public double DuckingThresholdDb
    {
        get => _engine.Ducker.ThresholdDb;
        set
        {
            _engine.Ducker.ThresholdDb = (float)value;
            _settings.DuckingThresholdDb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DuckingThresholdText));
        }
    }

    public string DuckingThresholdText => $"{DuckingThresholdDb:0} dB";

    public double DuckingReleaseMs
    {
        get => _engine.Ducker.ReleaseMs;
        set
        {
            _engine.Ducker.ReleaseMs = (float)value;
            _settings.DuckingReleaseMs = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DuckingReleaseText));
        }
    }

    public string DuckingReleaseText => $"{DuckingReleaseMs:0} ms";
}

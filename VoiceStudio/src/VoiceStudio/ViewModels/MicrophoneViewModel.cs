using System.Collections.ObjectModel;
using VoiceStudio.Audio;
using VoiceStudio.Audio.Services;
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
    private bool _isCapturingSwitchKey;
    private string? _currentMicrophoneId;
    private SwitchTarget? _switchTarget;
    private long _dropouts;
    private double _windowsLevel = -1;
    private string? _micWarning;
    private string? _testState;
    private MicrophoneTestResult? _testResult;

    public MicrophoneViewModel(AudioEngine engine, AppSettings settings, Action applyMonitor, Action hotkeysChanged)
    {
        _engine = engine;
        _settings = settings;
        _applyMonitor = applyMonitor;
        _hotkeysChanged = hotkeysChanged;

        engine.VoiceEffects.Chain.Gain = (float)Math.Pow(10, settings.MicrophoneGainDb / 20.0);

        var suppressor = engine.VoiceEffects.Chain.NoiseSuppression;
        suppressor.Enabled = settings.NoiseSuppressionEnabled;
        suppressor.Strength = (float)(settings.NoiseSuppressionStrength / 100.0);

        var autoLevel = engine.VoiceEffects.Chain.AutoLevel;
        autoLevel.Enabled = settings.AutoLevelEnabled;
        autoLevel.TargetDb = (float)settings.AutoLevelTargetDb;

        engine.Mixer.Engine.SoundboardDucker.Enabled = settings.SoundboardDuckingEnabled;
        engine.Mixer.Engine.SoundboardDucker.MinimumVolume = (float)(settings.SoundboardDuckingMinimum / 100.0);

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

        engine.SwitchEnabled = settings.SwitchEnabled;
        engine.SwitchTargetId = settings.SwitchTargetId;
        foreach (var bus in engine.Buses) SwitchTargets.Add(new SwitchTarget(bus.Id, bus.Name));
        SwitchTargets.Add(new SwitchTarget("soundboard", "Soundboard"));
        _switchTarget = SwitchTargets.FirstOrDefault(target => target.Id == settings.SwitchTargetId)
                        ?? SwitchTargets[0];

        CaptureKeyCommand = new RelayCommand(() => IsCapturingKey = true);
        ClearKeyCommand = new RelayCommand(() => SetPushToTalkKey(0, ""));
        StartTestCommand = new RelayCommand(() => _ = RunTestAsync(), () => !IsTestRunning);
        ApplySuggestedGainCommand = new RelayCommand(
            () => { if (TestResult != null) MicrophoneGainDb = Math.Round(TestResult.SuggestedGainDb); },
            () => TestResult != null);

        DismissWarningCommand = new RelayCommand(() =>
        {
            _settings.MicrophoneWarningDismissed = true;
            MicrophoneWarning = null;
        });
        CaptureSwitchKeyCommand = new RelayCommand(() => IsCapturingSwitchKey = true);
        ClearSwitchKeyCommand = new RelayCommand(() => SetSwitchKey(0, ""));
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
        ? "Coupures audio : aucune. Le son circule sans à-coup."
        : $"Coupures audio : {Dropouts}. Si ce nombre monte pendant que tu parles, c'est le transport du "
          + "son qui décroche : augmente la latence de sortie dans Réglages.";

    public void RefreshDropouts() => Dropouts = _engine.Dropouts;
    public RelayCommand ClearKeyCommand { get; }
    public RelayCommand StartTestCommand { get; }
    public RelayCommand ApplySuggestedGainCommand { get; }
    public RelayCommand DismissWarningCommand { get; }

    // --- Test du micro ---

    /// <summary>Où en est le test : message affiché pendant son déroulement.</summary>
    public string? TestState
    {
        get => _testState;
        private set
        {
            if (!SetProperty(ref _testState, value)) return;
            OnPropertyChanged(nameof(IsTestRunning));
            StartTestCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsTestRunning => TestState != null;

    public MicrophoneTestResult? TestResult
    {
        get => _testResult;
        private set
        {
            if (!SetProperty(ref _testResult, value)) return;
            OnPropertyChanged(nameof(HasTestResult));
            OnPropertyChanged(nameof(TestLevelsText));
            OnPropertyChanged(nameof(TestSuggestionText));
            ApplySuggestedGainCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasTestResult => TestResult != null;

    public string TestLevelsText => TestResult == null
        ? ""
        : $"Crête {TestResult.PeakDb:0} dB, moyenne {TestResult.RmsDb:0} dB.";

    public string TestSuggestionText => TestResult == null
        ? ""
        : Math.Abs(TestResult.SuggestedGainDb - MicrophoneGainDb) < 1
            ? TestResult.Verdict
            : $"{TestResult.Verdict} Gain conseillé : {TestResult.SuggestedGainDb:+0;-0} dB.";

    /// <summary>
    /// Enregistre cinq secondes de voix telle qu'elle part vers Discord, la réécoute
    /// dans le casque, puis propose un gain.
    /// </summary>
    private async Task RunTestAsync()
    {
        if (!_engine.IsRunning)
        {
            TestResult = null;
            TestState = null;
            MicrophoneWarning = "Démarre Voice Studio avant de lancer le test : il écoute ce qui part vraiment vers Discord.";
            return;
        }

        TestResult = null;
        try
        {
            _engine.StartMicrophoneTest(5);
            for (int remaining = 5; remaining > 0; remaining--)
            {
                TestState = $"Parle normalement… {remaining} s";
                await Task.Delay(1000).ConfigureAwait(true);
            }

            float[] samples = _engine.StopMicrophoneTest();
            TestResult = MicrophoneTestResult.Analyse(samples, (float)MicrophoneGainDb);

            TestState = "Réécoute : voilà ce que les autres entendent.";
            await _engine.PlayTestAsync(samples, _settings.MonitorId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MicrophoneWarning = "Le test n'a pas pu aller au bout : " + ex.Message;
        }
        finally
        {
            TestState = null;
        }
    }
    public RelayCommand CaptureSwitchKeyCommand { get; }
    public RelayCommand ClearSwitchKeyCommand { get; }

    // --- Niveau d'entrée Windows ---

    /// <summary>
    /// Volume d'entrée réglé dans Windows pour le micro choisi, en pourcentage.
    /// S'il est bas, aucun gain logiciel ne rattrapera proprement : c'est ici qu'il faut monter.
    /// </summary>
    public double WindowsInputLevel
    {
        get => _windowsLevel < 0 ? 100 : _windowsLevel;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 100);
            if (Math.Abs(WindowsInputLevel - clamped) < 0.5) return;
            _windowsLevel = clamped;
            if (_currentMicrophoneId != null)
                _engine.Devices.SetInputLevel(_currentMicrophoneId, (float)(clamped / 100.0));
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowsInputLevelText));
        }
    }

    public string WindowsInputLevelText => $"{WindowsInputLevel:0} %";

    public bool HasWindowsInputLevel => _windowsLevel >= 0;

    /// <summary>Avertissement quand le micro choisi est un périphérique virtuel.</summary>
    public string? MicrophoneWarning
    {
        get => _micWarning;
        set
        {
            if (!SetProperty(ref _micWarning, value)) return;
            OnPropertyChanged(nameof(HasMicrophoneWarning));
        }
    }

    public bool HasMicrophoneWarning => !string.IsNullOrEmpty(MicrophoneWarning);

    /// <summary>Appelé quand le micro sélectionné change.</summary>
    public void SetMicrophone(string? deviceId, string? deviceName)
    {
        _currentMicrophoneId = deviceId;
        float? level = deviceId == null ? null : _engine.Devices.GetInputLevel(deviceId);
        _windowsLevel = level.HasValue ? Math.Round(level.Value * 100.0) : -1;
        OnPropertyChanged(nameof(WindowsInputLevel));
        OnPropertyChanged(nameof(WindowsInputLevelText));
        OnPropertyChanged(nameof(HasWindowsInputLevel));

        MicrophoneWarning =
            !_settings.MicrophoneWarningDismissed && deviceName != null &&
            AudioDeviceService.LooksLikeVirtualInput(deviceName)
                ? "Ce micro vient d'un logiciel de routage, pas de la carte son. C'est parfait si tu veux "
                  + "garder son traitement — Voice Studio ne le remplace pas et lui laisse déjà plus de marge. "
                  + "Mais si tu entends des craquements, essaie ton micro physique pour comparer : ces entrées "
                  + "ont une horloge moins régulière."
                : null;
    }

    // --- Bascule micro / source ---

    public ObservableCollection<SwitchTarget> SwitchTargets { get; } = [];

    /// <summary>Remplacer le micro par une autre source en appuyant sur une touche.</summary>
    public bool SwitchEnabled
    {
        get => _engine.SwitchEnabled;
        set
        {
            _engine.SwitchEnabled = value;
            _settings.SwitchEnabled = value;
            OnPropertyChanged();
            _hotkeysChanged();
        }
    }

    public SwitchTarget? SelectedSwitchTarget
    {
        get => _switchTarget;
        set
        {
            if (!SetProperty(ref _switchTarget, value) || value == null) return;
            _settings.SwitchTargetId = value.Id;
            _engine.SwitchTargetId = value.Id;
        }
    }

    /// <summary>Vrai : il faut maintenir la touche. Faux : un appui bascule, un autre revient.</summary>
    public bool SwitchHold
    {
        get => _settings.SwitchHold;
        set
        {
            _settings.SwitchHold = value;
            if (value) _engine.SwitchActive = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SwitchModeText));
        }
    }

    public string SwitchModeText => SwitchHold
        ? "Maintiens la touche pour envoyer la source à la place du micro."
        : "Un appui envoie la source, un autre revient au micro.";

    public int SwitchVirtualKey => _settings.SwitchVirtualKey;

    public string SwitchKeyText => _settings.SwitchKeyName.Length > 0
        ? _settings.SwitchKeyName
        : "Aucune touche";

    public bool IsCapturingSwitchKey
    {
        get => _isCapturingSwitchKey;
        set => SetProperty(ref _isCapturingSwitchKey, value);
    }

    public void SetSwitchKey(int virtualKey, string name)
    {
        _settings.SwitchVirtualKey = virtualKey;
        _settings.SwitchKeyName = name;
        IsCapturingSwitchKey = false;
        OnPropertyChanged(nameof(SwitchKeyText));
        _hotkeysChanged();
    }

    // --- Gain ---

    /// <summary>Gain du micro en décibels : à monter si on t'entend trop bas sur Discord.</summary>
    public double MicrophoneGainDb
    {
        get => _settings.MicrophoneGainDb;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), -12, 24);
            if (Math.Abs(_settings.MicrophoneGainDb - clamped) < 0.01) return;
            _settings.MicrophoneGainDb = clamped;
            _engine.VoiceEffects.Chain.Gain = (float)Math.Pow(10, clamped / 20.0);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MicrophoneGainText));
        }
    }

    public string MicrophoneGainText => MicrophoneGainDb == 0
        ? "0 dB — niveau d'origine"
        : $"{MicrophoneGainDb:+0;-0} dB";

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

    // --- Niveau automatique ---

    /// <summary>Maintient ta voix à un niveau constant, que tu parles fort ou doucement.</summary>
    public bool AutoLevelEnabled
    {
        get => _engine.VoiceEffects.Chain.AutoLevel.Enabled;
        set
        {
            _engine.VoiceEffects.Chain.AutoLevel.Enabled = value;
            _settings.AutoLevelEnabled = value;
            OnPropertyChanged();
        }
    }

    public double AutoLevelTargetDb
    {
        get => _engine.VoiceEffects.Chain.AutoLevel.TargetDb;
        set
        {
            _engine.VoiceEffects.Chain.AutoLevel.TargetDb = (float)value;
            _settings.AutoLevelTargetDb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoLevelTargetText));
        }
    }

    public string AutoLevelTargetText => $"{AutoLevelTargetDb:0} dB";

    // --- Baisse pendant un extrait ---

    /// <summary>Baisser les sons du PC pendant qu'un extrait du soundboard joue.</summary>
    public bool SoundboardDuckingEnabled
    {
        get => _engine.Mixer.Engine.SoundboardDucker.Enabled;
        set
        {
            _engine.Mixer.Engine.SoundboardDucker.Enabled = value;
            _settings.SoundboardDuckingEnabled = value;
            OnPropertyChanged();
        }
    }

    public double SoundboardDuckingMinimum
    {
        get => Math.Round(_engine.Mixer.Engine.SoundboardDucker.MinimumVolume * 100.0);
        set
        {
            _engine.Mixer.Engine.SoundboardDucker.MinimumVolume = (float)(value / 100.0);
            _settings.SoundboardDuckingMinimum = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SoundboardDuckingMinimumText));
        }
    }

    public string SoundboardDuckingMinimumText => $"{SoundboardDuckingMinimum:0} %";

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


/// <summary>Une source qui peut remplacer le micro : un bus, ou le soundboard.</summary>
public sealed record SwitchTarget(string Id, string Name)
{
    public override string ToString() => Name;
}

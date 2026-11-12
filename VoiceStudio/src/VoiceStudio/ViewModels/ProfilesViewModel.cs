using System.Collections.ObjectModel;
using VoiceStudio.Helpers;
using VoiceStudio.Settings;

namespace VoiceStudio.ViewModels;

/// <summary>Écran des profils : rappeler un jeu de réglages complet d'un clic.</summary>
public sealed class ProfilesViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly MixerViewModel _mixer;
    private readonly MicrophoneViewModel _microphone;
    private readonly VoiceFxViewModel _voiceFx;
    private Profile? _selected;
    private string _newName = "";
    private string? _message;

    public ProfilesViewModel(AppSettings settings, MixerViewModel mixer,
        MicrophoneViewModel microphone, VoiceFxViewModel voiceFx)
    {
        _settings = settings;
        _mixer = mixer;
        _microphone = microphone;
        _voiceFx = voiceFx;

        foreach (var profile in Profile.CreateBuiltIns()) Profiles.Add(profile);
        foreach (var profile in settings.CustomProfiles) Profiles.Add(profile);
        _selected = Profiles.FirstOrDefault(profile => profile.Name == settings.SelectedProfile);

        ApplyCommand = new RelayCommand(Apply, () => Selected != null);
        UpdateCommand = new RelayCommand(Update, () => Selected is { IsBuiltIn: false });
        DeleteCommand = new RelayCommand(Delete, () => Selected is { IsBuiltIn: false });
        CreateCommand = new RelayCommand(Create, () => NewName.Trim().Length > 0);
    }

    public ObservableCollection<Profile> Profiles { get; } = [];

    public RelayCommand ApplyCommand { get; }
    public RelayCommand UpdateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand CreateCommand { get; }

    public Profile? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            ApplyCommand.RaiseCanExecuteChanged();
            UpdateCommand.RaiseCanExecuteChanged();
            DeleteCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Nom du profil à créer à partir des réglages actuels.</summary>
    public string NewName
    {
        get => _newName;
        set
        {
            if (!SetProperty(ref _newName, value)) return;
            CreateCommand.RaiseCanExecuteChanged();
        }
    }

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

    /// <summary>Applique un profil : tous les réglages qu'il contient sont écrits d'un coup.</summary>
    public void Apply(Profile profile)
    {
        _mixer.Microphone.Volume = profile.MicrophoneVolume;
        _mixer.Soundboard.Volume = profile.SoundboardVolume;
        _mixer.MasterVolume = profile.MasterVolume;
        foreach (var bus in _mixer.Buses)
            if (profile.BusVolumes.TryGetValue(bus.Id, out double volume))
                bus.Volume = volume;
        _mixer.RefreshAppsVolume();

        _microphone.MicrophoneGainDb = profile.MicrophoneGainDb;
        _microphone.NoiseSuppressionEnabled = profile.NoiseSuppressionEnabled;
        _microphone.NoiseSuppressionStrength = profile.NoiseSuppressionStrength;
        _microphone.NoiseGateEnabled = profile.NoiseGateEnabled;
        _microphone.NoiseGateThresholdDb = profile.NoiseGateThresholdDb;
        _microphone.DuckingEnabled = profile.DuckingEnabled;
        _microphone.DuckingMinimumVolume = profile.DuckingMinimumVolume;

        _voiceFx.EffectsEnabled = profile.EffectsEnabled;
        if (profile.PresetName != null)
        {
            var preset = _voiceFx.Presets.FirstOrDefault(item => item.Name == profile.PresetName);
            if (preset != null) _voiceFx.SelectedPreset = preset;
        }

        _settings.SelectedProfile = profile.Name;
        Message = $"Profil « {profile.Name} » appliqué.";
    }

    private void Apply()
    {
        if (Selected != null) Apply(Selected);
    }

    /// <summary>Photographie les réglages actuels dans un profil.</summary>
    private Profile Capture(string name) => new()
    {
        Name = name,
        MicrophoneVolume = _mixer.Microphone.Volume,
        SoundboardVolume = _mixer.Soundboard.Volume,
        MasterVolume = _mixer.MasterVolume,
        BusVolumes = _mixer.Buses.ToDictionary(bus => bus.Id, bus => bus.Volume,
            StringComparer.OrdinalIgnoreCase),
        MicrophoneGainDb = _microphone.MicrophoneGainDb,
        NoiseSuppressionEnabled = _microphone.NoiseSuppressionEnabled,
        NoiseSuppressionStrength = _microphone.NoiseSuppressionStrength,
        NoiseGateEnabled = _microphone.NoiseGateEnabled,
        NoiseGateThresholdDb = _microphone.NoiseGateThresholdDb,
        DuckingEnabled = _microphone.DuckingEnabled,
        DuckingMinimumVolume = _microphone.DuckingMinimumVolume,
        EffectsEnabled = _voiceFx.EffectsEnabled,
        PresetName = _voiceFx.SelectedPreset?.Name
    };

    private void Create()
    {
        string name = NewName.Trim();
        var existing = Profiles.FirstOrDefault(
            profile => !profile.IsBuiltIn && string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));
        var captured = Capture(name);

        if (existing != null)
        {
            _settings.CustomProfiles.Remove(existing);
            Profiles[Profiles.IndexOf(existing)] = captured;
        }
        else
        {
            Profiles.Add(captured);
        }
        _settings.CustomProfiles.Add(captured);
        _settings.SelectedProfile = name;
        Selected = captured;
        NewName = "";
        Message = $"Profil « {name} » créé à partir de tes réglages actuels.";
    }

    private void Update()
    {
        if (Selected is not { IsBuiltIn: false } profile) return;
        var captured = Capture(profile.Name);
        int index = Profiles.IndexOf(profile);
        _settings.CustomProfiles.Remove(profile);
        _settings.CustomProfiles.Add(captured);
        Profiles[index] = captured;
        Selected = captured;
        Message = $"Profil « {captured.Name} » mis à jour.";
    }

    private void Delete()
    {
        if (Selected is not { IsBuiltIn: false } profile) return;
        Profiles.Remove(profile);
        _settings.CustomProfiles.Remove(profile);
        Selected = null;
        Message = $"Profil « {profile.Name} » supprimé.";
    }
}

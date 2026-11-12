using System.Collections.ObjectModel;
using VoiceStudio.Audio.Effects;
using VoiceStudio.Audio.Services;
using VoiceStudio.Helpers;
using VoiceStudio.Settings;

namespace VoiceStudio.ViewModels;

/// <summary>Un réglage d'effet (fréquence, intensité...), relié au moteur.</summary>
public sealed class EffectParameterViewModel(EffectParameter parameter) : ObservableObject
{
    public string Name => parameter.Name;
    public double Minimum => parameter.Min;
    public double Maximum => parameter.Max;

    public double Value
    {
        get => parameter.Value;
        set
        {
            parameter.Value = (float)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValueText));
        }
    }

    public string ValueText => parameter.Unit.Length > 0
        ? $"{parameter.Value:0.##} {parameter.Unit}"
        : $"{parameter.Value:0.##}";

    public void Refresh()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(ValueText));
    }
}

/// <summary>Un effet vocal : son interrupteur et ses réglages.</summary>
public sealed class EffectViewModel : ObservableObject
{
    private readonly VoiceEffect _effect;

    public EffectViewModel(VoiceEffect effect)
    {
        _effect = effect;
        Parameters = new ObservableCollection<EffectParameterViewModel>(
            effect.Parameters.Select(parameter => new EffectParameterViewModel(parameter)));
    }

    public string Name => _effect.Name;
    public ObservableCollection<EffectParameterViewModel> Parameters { get; }
    public bool HasParameters => Parameters.Count > 0;

    public bool IsEnabled
    {
        get => _effect.Enabled;
        set
        {
            _effect.Enabled = value;
            OnPropertyChanged();
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsEnabled));
        foreach (var parameter in Parameters) parameter.Refresh();
    }
}

/// <summary>Écran « Voix » : effets vocaux et presets.</summary>
public sealed class VoiceFxViewModel : ObservableObject
{
    private readonly VoiceEffectsService _service;
    private readonly AppSettings _settings;
    private VoicePreset? _selectedPreset;
    private string _newPresetName = "";

    public VoiceFxViewModel(VoiceEffectsService service, AppSettings settings)
    {
        _service = service;
        _settings = settings;

        Effects = new ObservableCollection<EffectViewModel>(
            service.Chain.Effects.Select(effect => new EffectViewModel(effect)));

        foreach (var preset in VoicePreset.CreateBuiltIns()) Presets.Add(preset);
        foreach (var preset in settings.CustomPresets) Presets.Add(preset);

        service.Chain.EffectsEnabled = settings.EffectsEnabled;
        _selectedPreset = Presets.FirstOrDefault(preset => preset.Name == settings.SelectedPreset)
                          ?? Presets.FirstOrDefault();
        if (_selectedPreset != null) service.ApplyPreset(_selectedPreset);

        ResetCommand = new RelayCommand(Reset);
        RandomCommand = new RelayCommand(PickRandomPreset, () => Presets.Count > 1);
        SavePresetCommand = new RelayCommand(SavePreset, () => NewPresetName.Trim().Length > 0);
        DeletePresetCommand = new RelayCommand(DeletePreset,
            () => SelectedPreset is { IsBuiltIn: false });
    }

    public ObservableCollection<EffectViewModel> Effects { get; }
    public ObservableCollection<VoicePreset> Presets { get; } = [];

    public RelayCommand ResetCommand { get; }
    public RelayCommand RandomCommand { get; }
    public RelayCommand SavePresetCommand { get; }
    public RelayCommand DeletePresetCommand { get; }

    /// <summary>Interrupteur général des effets.</summary>
    public bool EffectsEnabled
    {
        get => _service.Chain.EffectsEnabled;
        set
        {
            _service.Chain.EffectsEnabled = value;
            _settings.EffectsEnabled = value;
            OnPropertyChanged();
        }
    }

    public VoicePreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!SetProperty(ref _selectedPreset, value) || value == null) return;
            _service.ApplyPreset(value);
            _settings.SelectedPreset = value.Name;
            RefreshEffects();
            DeletePresetCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Nom proposé pour enregistrer les réglages actuels.</summary>
    public string NewPresetName
    {
        get => _newPresetName;
        set
        {
            if (!SetProperty(ref _newPresetName, value)) return;
            SavePresetCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Choisit un preset au hasard, différent de celui en cours.</summary>
    private void PickRandomPreset()
    {
        var others = Presets.Where(preset => preset != SelectedPreset).ToList();
        if (others.Count == 0) return;
        SelectedPreset = others[Random.Shared.Next(others.Count)];
    }

    private void Reset()
    {
        _service.Reset();
        RefreshEffects();
    }

    private void SavePreset()
    {
        string name = NewPresetName.Trim();
        var preset = _service.CapturePreset(name);

        var existing = Presets.FirstOrDefault(
            item => !item.IsBuiltIn && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            Presets[Presets.IndexOf(existing)] = preset;
            _settings.CustomPresets.Remove(existing);
        }
        else
        {
            Presets.Add(preset);
        }

        _settings.CustomPresets.Add(preset);
        _settings.SelectedPreset = name;
        NewPresetName = "";
        SetProperty(ref _selectedPreset, preset, nameof(SelectedPreset));
        DeletePresetCommand.RaiseCanExecuteChanged();
    }

    private void DeletePreset()
    {
        if (SelectedPreset is not { IsBuiltIn: false } preset) return;
        Presets.Remove(preset);
        _settings.CustomPresets.Remove(preset);
        SelectedPreset = Presets.FirstOrDefault();
    }

    private void RefreshEffects()
    {
        foreach (var effect in Effects) effect.Refresh();
    }
}

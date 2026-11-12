using System.Collections.ObjectModel;
using VoiceStudio.Audio.Mixing;
using VoiceStudio.Helpers;

namespace VoiceStudio.ViewModels;

/// <summary>
/// Un bus du mixeur (Jeu, Chat, Média, Autres) : son volume, son bouton couper,
/// et les applications qu'on y a rangées.
/// </summary>
public sealed class BusViewModel : ObservableObject
{
    private readonly AudioBus _bus;
    private double _level;

    public BusViewModel(AudioBus bus, string accentKey)
    {
        _bus = bus;
        AccentKey = accentKey;
    }

    public string Id => _bus.Id;
    public string Name => _bus.Name;

    /// <summary>Nom de la couleur à utiliser dans l'interface.</summary>
    public string AccentKey { get; }

    /// <summary>Les applications rangées dans ce bus.</summary>
    public ObservableCollection<ChannelViewModel> Apps { get; } = [];

    public bool IsEmpty => Apps.Count == 0;

    public double Volume
    {
        get => Math.Round(_bus.Channel.Volume * 100.0);
        set
        {
            _bus.Channel.Volume = (float)(value / 100.0);
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => $"{Volume:0} %";

    public bool IsMuted
    {
        get => _bus.Channel.Muted;
        set
        {
            _bus.Channel.Muted = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Écouter ce bus seul : tous les autres se taisent.</summary>
    public bool IsSolo
    {
        get => _bus.Channel.Solo;
        set
        {
            _bus.Channel.Solo = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Forcer ce bus au centre, quand le son n'arrive que d'un côté.</summary>
    public bool IsMono
    {
        get => _bus.Channel.Mono;
        set
        {
            _bus.Channel.Mono = value;
            OnPropertyChanged();
        }
    }

    public double Level
    {
        get => _level;
        private set => SetProperty(ref _level, value);
    }

    public void RefreshLevel()
    {
        Level = MeterScale.ToMeter(_bus.Channel.Peak);
        foreach (var app in Apps) app.RefreshLevel();
    }

    public void SetApps(IEnumerable<ChannelViewModel> apps)
    {
        Apps.Clear();
        foreach (var app in apps) Apps.Add(app);
        OnPropertyChanged(nameof(IsEmpty));
    }
}

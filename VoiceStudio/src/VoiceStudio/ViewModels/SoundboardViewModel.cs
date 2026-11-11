using System.Collections.ObjectModel;
using System.IO;
using VoiceStudio.Audio.Services;
using VoiceStudio.Helpers;
using VoiceStudio.Settings;

namespace VoiceStudio.ViewModels;

/// <summary>Un son du soundboard.</summary>
public sealed class SoundViewModel(SoundEntry entry) : ObservableObject
{
    private bool _isPlaying;
    private bool _isMissing;
    private bool _isCapturingKey;

    public SoundEntry Entry { get; } = entry;
    public string Id => Entry.Id;
    public string Path => Entry.Path;

    public string Name
    {
        get => Entry.Name;
        set
        {
            Entry.Name = value;
            OnPropertyChanged();
        }
    }

    public double Volume
    {
        get => Entry.Volume;
        set
        {
            Entry.Volume = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => $"{Volume:0} %";

    public string HotkeyText => Entry.HotkeyName.Length > 0 ? Entry.HotkeyName : "Pas de touche";

    public int HotkeyVirtualKey => Entry.HotkeyVirtualKey;

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    /// <summary>Le fichier n'existe plus à l'emplacement enregistré.</summary>
    public bool IsMissing
    {
        get => _isMissing;
        set => SetProperty(ref _isMissing, value);
    }

    /// <summary>L'interface attend que l'utilisateur appuie sur une touche.</summary>
    public bool IsCapturingKey
    {
        get => _isCapturingKey;
        set => SetProperty(ref _isCapturingKey, value);
    }

    public void SetHotkey(int virtualKey, string name)
    {
        Entry.HotkeyVirtualKey = virtualKey;
        Entry.HotkeyName = name;
        IsCapturingKey = false;
        OnPropertyChanged(nameof(HotkeyText));
    }
}

/// <summary>Écran « Soundboard » : bibliothèque de sons jouée dans le micro virtuel.</summary>
public sealed class SoundboardViewModel : ObservableObject
{
    private readonly SoundboardService _service;
    private readonly AppSettings _settings;
    private readonly IFilePicker _filePicker;
    private readonly Action _hotkeysChanged;
    private string? _message;

    public SoundboardViewModel(SoundboardService service, AppSettings settings,
        IFilePicker filePicker, Action hotkeysChanged)
    {
        _service = service;
        _settings = settings;
        _filePicker = filePicker;
        _hotkeysChanged = hotkeysChanged;

        service.Mixer.MaxSimultaneous = settings.MaxSimultaneousSounds;
        foreach (var entry in settings.Sounds)
            Sounds.Add(new SoundViewModel(entry) { IsMissing = !File.Exists(entry.Path) });

        AddCommand = new RelayCommand(AddSounds);
        StopAllCommand = new RelayCommand(StopAll);
        PlayCommand = new RelayCommand<SoundViewModel>(sound => _ = PlayAsync(sound));
        StopCommand = new RelayCommand<SoundViewModel>(Stop);
        RemoveCommand = new RelayCommand<SoundViewModel>(Remove);
        CaptureKeyCommand = new RelayCommand<SoundViewModel>(sound => sound.IsCapturingKey = true);
        ClearKeyCommand = new RelayCommand<SoundViewModel>(sound =>
        {
            sound.SetHotkey(0, "");
            _hotkeysChanged();
        });
    }

    public ObservableCollection<SoundViewModel> Sounds { get; } = [];

    public RelayCommand AddCommand { get; }
    public RelayCommand StopAllCommand { get; }
    public RelayCommand<SoundViewModel> PlayCommand { get; }
    public RelayCommand<SoundViewModel> StopCommand { get; }
    public RelayCommand<SoundViewModel> RemoveCommand { get; }
    public RelayCommand<SoundViewModel> CaptureKeyCommand { get; }
    public RelayCommand<SoundViewModel> ClearKeyCommand { get; }

    public bool HasSounds => Sounds.Count > 0;

    /// <summary>Message d'information ou d'erreur affiché sous la liste.</summary>
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

    /// <summary>Nombre maximum de sons joués en même temps.</summary>
    public int MaxSimultaneous
    {
        get => _service.Mixer.MaxSimultaneous;
        set
        {
            _service.Mixer.MaxSimultaneous = value;
            _settings.MaxSimultaneousSounds = _service.Mixer.MaxSimultaneous;
            OnPropertyChanged();
        }
    }

    public void SetHotkey(SoundViewModel sound, int virtualKey, string name)
    {
        sound.SetHotkey(virtualKey, name);
        _hotkeysChanged();
    }

    public SoundViewModel? FindByHotkey(int virtualKey) =>
        virtualKey == 0 ? null : Sounds.FirstOrDefault(sound => sound.HotkeyVirtualKey == virtualKey);

    public IEnumerable<int> HotkeyVirtualKeys =>
        Sounds.Select(sound => sound.HotkeyVirtualKey).Where(key => key != 0);

    private void AddSounds()
    {
        var files = _filePicker.PickAudioFiles();
        int added = 0;
        foreach (string path in files)
        {
            if (Sounds.Any(sound => string.Equals(sound.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            var entry = new SoundEntry { Name = System.IO.Path.GetFileNameWithoutExtension(path), Path = path };
            _settings.Sounds.Add(entry);
            Sounds.Add(new SoundViewModel(entry));
            added++;
        }
        if (added > 0)
        {
            Message = null;
            OnPropertyChanged(nameof(HasSounds));
        }
    }

    public async Task PlayAsync(SoundViewModel sound)
    {
        if (!File.Exists(sound.Path))
        {
            sound.IsMissing = true;
            Message = $"Fichier introuvable : {sound.Path}";
            return;
        }

        try
        {
            sound.IsMissing = false;
            bool started = await _service.PlayAsync(sound.Id, sound.Path, (float)(sound.Volume / 100.0));
            if (!started)
            {
                Message = $"Limite atteinte : {MaxSimultaneous} sons en même temps.";
                return;
            }
            Message = null;
            sound.IsPlaying = true;
        }
        catch (Exception ex)
        {
            Message = $"Lecture impossible ({sound.Name}) : {ex.Message}";
        }
    }

    private void Stop(SoundViewModel sound)
    {
        _service.Stop(sound.Id);
        sound.IsPlaying = false;
    }

    private void StopAll()
    {
        _service.StopAll();
        foreach (var sound in Sounds) sound.IsPlaying = false;
    }

    private void Remove(SoundViewModel sound)
    {
        _service.Stop(sound.Id);
        _service.Forget(sound.Path);
        _settings.Sounds.Remove(sound.Entry);
        Sounds.Remove(sound);
        OnPropertyChanged(nameof(HasSounds));
        _hotkeysChanged();
    }

    /// <summary>Met à jour l'état « en cours de lecture » (appelé par le rafraîchissement de l'interface).</summary>
    public void RefreshPlayingState()
    {
        foreach (var sound in Sounds)
        {
            bool playing = _service.Mixer.IsPlaying(sound.Id);
            if (sound.IsPlaying != playing) sound.IsPlaying = playing;
        }
    }
}

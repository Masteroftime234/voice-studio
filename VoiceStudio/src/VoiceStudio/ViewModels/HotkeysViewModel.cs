using System.Collections.ObjectModel;
using VoiceStudio.Helpers;
using VoiceStudio.Settings;

namespace VoiceStudio.ViewModels;

/// <summary>Une action et la touche qui la déclenche.</summary>
public sealed class HotkeyViewModel : ObservableObject
{
    private readonly HotkeyBinding _binding;
    private bool _isCapturing;

    public HotkeyViewModel(string id, string label, string description, HotkeyBinding binding)
    {
        Id = id;
        Label = label;
        Description = description;
        _binding = binding;
    }

    public string Id { get; }
    public string Label { get; }
    public string Description { get; }

    public int VirtualKey => _binding.VirtualKey;

    public string KeyText => _binding.KeyName.Length > 0 ? _binding.KeyName : "Aucune touche";

    /// <summary>L'interface attend que l'utilisateur appuie sur une touche.</summary>
    public bool IsCapturing
    {
        get => _isCapturing;
        set => SetProperty(ref _isCapturing, value);
    }

    public void SetKey(int virtualKey, string name)
    {
        _binding.VirtualKey = virtualKey;
        _binding.KeyName = name;
        IsCapturing = false;
        OnPropertyChanged(nameof(KeyText));
    }
}

/// <summary>Écran des raccourcis : couper le micro, couper les sons, régler le volume.</summary>
public sealed class HotkeysViewModel : ObservableObject
{
    public const string MuteMicrophone = "mute-micro";
    public const string MuteSounds = "mute-sons";
    public const string VolumeUp = "volume-plus";
    public const string VolumeDown = "volume-moins";

    private readonly Action _changed;

    public HotkeysViewModel(AppSettings settings, Action changed)
    {
        _changed = changed;

        Add(settings, MuteMicrophone, "Couper le micro",
            "Coupe et réactive ton micro, où que tu sois.");
        Add(settings, MuteSounds, "Couper les sons du PC",
            "Met tous les bus en silence d'un coup, sans toucher à ta voix.");
        Add(settings, VolumeUp, "Monter le volume général", "Par pas de 5 %.");
        Add(settings, VolumeDown, "Baisser le volume général", "Par pas de 5 %.");

        CaptureCommand = new RelayCommand<HotkeyViewModel>(hotkey => hotkey.IsCapturing = true);
        ClearCommand = new RelayCommand<HotkeyViewModel>(hotkey =>
        {
            hotkey.SetKey(0, "");
            _changed();
        });
    }

    public ObservableCollection<HotkeyViewModel> Items { get; } = [];

    public RelayCommand<HotkeyViewModel> CaptureCommand { get; }
    public RelayCommand<HotkeyViewModel> ClearCommand { get; }

    public IEnumerable<int> VirtualKeys => Items.Select(item => item.VirtualKey).Where(key => key != 0);

    public HotkeyViewModel? FindByKey(int virtualKey) =>
        virtualKey == 0 ? null : Items.FirstOrDefault(item => item.VirtualKey == virtualKey);

    public HotkeyViewModel? Capturing => Items.FirstOrDefault(item => item.IsCapturing);

    public void SetKey(HotkeyViewModel hotkey, int virtualKey, string name)
    {
        hotkey.SetKey(virtualKey, name);
        _changed();
    }

    private void Add(AppSettings settings, string id, string label, string description)
    {
        if (!settings.Hotkeys.TryGetValue(id, out var binding))
        {
            binding = new HotkeyBinding();
            settings.Hotkeys[id] = binding;
        }
        Items.Add(new HotkeyViewModel(id, label, description, binding));
    }
}

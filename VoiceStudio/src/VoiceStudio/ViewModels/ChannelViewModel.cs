using VoiceStudio.Audio.Mixing;
using VoiceStudio.Helpers;

namespace VoiceStudio.ViewModels;

/// <summary>
/// Une tranche du mixeur, vue par l'interface : un nom, un volume, un bouton couper
/// et un indicateur de niveau. Elle pilote soit une tranche fixe (micro, soundboard),
/// soit une application capturée.
/// </summary>
public sealed class ChannelViewModel : ObservableObject
{
    private readonly Func<float> _readVolume;
    private readonly Action<float> _writeVolume;
    private readonly Func<bool> _readMuted;
    private readonly Action<bool> _writeMuted;
    private readonly Func<float> _readPeak;
    private double _level;

    public ChannelViewModel(MixerChannel channel, string title, string subtitle)
        : this(() => channel.Volume, volume => channel.Volume = volume,
               () => channel.Muted, muted => channel.Muted = muted,
               () => channel.Peak, title, subtitle)
    {
    }

    public ChannelViewModel(AppAudioSource source)
        : this(() => source.Volume, volume => source.Volume = volume,
               () => source.Muted, muted => source.Muted = muted,
               () => source.Peak, FriendlyAppName.For(source.Name), source.Name)
    {
        AppName = source.Name;
    }

    private ChannelViewModel(Func<float> readVolume, Action<float> writeVolume,
        Func<bool> readMuted, Action<bool> writeMuted, Func<float> readPeak,
        string title, string subtitle)
    {
        _readVolume = readVolume;
        _writeVolume = writeVolume;
        _readMuted = readMuted;
        _writeMuted = writeMuted;
        _readPeak = readPeak;
        Title = title;
        Subtitle = subtitle;
    }

    public string Title { get; }

    public string Subtitle { get; }

    /// <summary>Nom du processus, pour les tranches d'application (null sinon).</summary>
    public string? AppName { get; }

    /// <summary>Volume en pourcentage : 0 à 150.</summary>
    public double Volume
    {
        get => Math.Round(_readVolume() * 100.0);
        set
        {
            _writeVolume((float)(value / 100.0));
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => $"{Volume:0} %";

    public bool IsMuted
    {
        get => _readMuted();
        set
        {
            _writeMuted(value);
            OnPropertyChanged();
        }
    }

    /// <summary>Niveau affiché par l'indicateur : 0 à 1 (échelle en décibels).</summary>
    public double Level
    {
        get => _level;
        private set => SetProperty(ref _level, value);
    }

    public void RefreshLevel() => Level = MeterScale.ToMeter(_readPeak());
}

public static class MeterScale
{
    /// <summary>Convertit une crête linéaire en position 0–1 sur une échelle de -60 dB à 0 dB.</summary>
    public static double ToMeter(float peak)
    {
        if (peak <= 0.001f) return 0;
        double decibels = 20.0 * Math.Log10(peak);
        return Math.Clamp((decibels + 60.0) / 60.0, 0, 1);
    }
}

/// <summary>Noms d'applications lisibles à partir du nom du processus Windows.</summary>
public static class FriendlyAppName
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome",
        ["msedge"] = "Edge",
        ["firefox"] = "Firefox",
        ["opera"] = "Opera",
        ["brave"] = "Brave",
        ["vivaldi"] = "Vivaldi",
        ["spotify"] = "Spotify",
        ["vlc"] = "VLC",
        ["steam"] = "Steam",
        ["steamwebhelper"] = "Steam",
        ["audiodg"] = "Windows",
        ["explorer"] = "Windows",
        ["searchfilterhost"] = "Windows",
        ["wmplayer"] = "Lecteur Windows Media",
        ["applicationframehost"] = "Application Windows",
        ["steelseriessonar"] = "SteelSeries Sonar",
        ["steelseriesggclient"] = "SteelSeries GG",
        ["obs64"] = "OBS",
        ["javaw"] = "Minecraft"
    };

    public static string For(string processName)
    {
        if (Known.TryGetValue(processName, out string? friendly)) return friendly;
        if (processName.Length == 0) return processName;
        return char.ToUpperInvariant(processName[0]) + processName[1..];
    }
}

using VoiceStudio.Audio.Mixing;
using VoiceStudio.Helpers;

namespace VoiceStudio.ViewModels;

/// <summary>Une tranche de la table de mixage, vue par l'interface.</summary>
public sealed class ChannelStripViewModel : ObservableObject
{
    private readonly MixerChannel _channel;
    private double _level;

    public ChannelStripViewModel(MixerChannel channel, string title, string subtitle)
    {
        _channel = channel;
        Title = title;
        Subtitle = subtitle;
    }

    public string Title { get; }
    public string Subtitle { get; }

    /// <summary>Volume en pourcentage : 0 à 150.</summary>
    public double Volume
    {
        get => Math.Round(_channel.Volume * 100.0);
        set
        {
            _channel.Volume = (float)(value / 100.0);
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => $"{Volume:0} %";

    public bool IsMuted
    {
        get => _channel.Muted;
        set
        {
            _channel.Muted = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Niveau affiché par l'indicateur : 0 à 1 (échelle en décibels).</summary>
    public double Level
    {
        get => _level;
        private set => SetProperty(ref _level, value);
    }

    public void RefreshLevel() => Level = MeterScale.ToMeter(_channel.Peak);
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

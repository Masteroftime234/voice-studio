using System.Windows;
using System.Windows.Input;
using VoiceStudio.ViewModels;

namespace VoiceStudio;

/// <summary>
/// Petit panneau flottant, indépendant de la fenêtre principale : il reste accessible
/// par-dessus un jeu pour couper le micro ou changer un volume.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly MainViewModel _model;

    public OverlayWindow(MainViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;

        var settings = model.Settings;
        Topmost = settings.OverlayTopmost;
        Opacity = Math.Clamp(settings.OverlayOpacity, 20, 100) / 100.0;
        Width = Math.Max(MinWidth, settings.OverlayWidth);
        Height = Math.Max(MinHeight, settings.OverlayHeight);

        if (settings.OverlayLeft >= 0 && settings.OverlayTop >= 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = settings.OverlayLeft;
            Top = settings.OverlayTop;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    /// <summary>Reprend les réglages d'apparence après un changement dans la page Réglages.</summary>
    public void ApplyAppearance()
    {
        Topmost = _model.Settings.OverlayTopmost;
        Opacity = Math.Clamp(_model.Settings.OverlayOpacity, 20, 100) / 100.0;
    }

    /// <summary>Mémorise la position et la taille pour la prochaine fois.</summary>
    public void SavePlacement()
    {
        if (WindowState != WindowState.Normal) return;
        _model.Settings.OverlayLeft = Left;
        _model.Settings.OverlayTop = Top;
        _model.Settings.OverlayWidth = Width;
        _model.Settings.OverlayHeight = Height;
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        SavePlacement();
    }

    private void OnClose(object sender, RoutedEventArgs e) => _model.IsOverlayVisible = false;
}

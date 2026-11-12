using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VoiceStudio.ViewModels;

namespace VoiceStudio;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _meterTimer;
    private OverlayWindow? _overlay;

    public MainWindow()
    {
        InitializeComponent();

        // Les indicateurs sont rafraîchis par l'interface (~30 fois/s) :
        // le thread audio ne touche jamais à l'affichage.
        _meterTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _meterTimer.Tick += (_, _) => (DataContext as MainViewModel)?.RefreshMeters();

        Loaded += (_, _) =>
        {
            _meterTimer.Start();
            AttachModel();
        };
        DataContextChanged += (_, _) => AttachModel();
        Closed += (_, _) =>
        {
            _meterTimer.Stop();
            CloseOverlay();
        };
        StateChanged += OnWindowStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private MainViewModel? _model;

    /// <summary>Relie la fenêtre au modèle : panneau flottant et réglages d'apparence.</summary>
    private void AttachModel()
    {
        if (DataContext is not MainViewModel model || ReferenceEquals(model, _model)) return;

        if (_model != null)
        {
            _model.PropertyChanged -= OnModelPropertyChanged;
            _model.SettingsPage.OverlayAppearanceChanged -= OnOverlayAppearanceChanged;
        }

        _model = model;
        model.PropertyChanged += OnModelPropertyChanged;
        model.SettingsPage.OverlayAppearanceChanged += OnOverlayAppearanceChanged;
        UpdateOverlay();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsOverlayVisible)) UpdateOverlay();
    }

    private void OnOverlayAppearanceChanged() => _overlay?.ApplyAppearance();

    private void UpdateOverlay()
    {
        if (_model == null) return;

        if (!_model.IsOverlayVisible)
        {
            CloseOverlay();
            return;
        }

        if (_overlay == null)
        {
            _overlay = new OverlayWindow(_model) { Owner = null };
            _overlay.Closed += (_, _) => _overlay = null;
        }
        _overlay.Show();
        _overlay.Activate();
    }

    private void CloseOverlay()
    {
        if (_overlay == null) return;
        _overlay.SavePlacement();
        var overlay = _overlay;
        _overlay = null;
        overlay.Close();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // Sans bordure, une fenêtre agrandie déborde de l'écran : on compense.
        bool maximized = WindowState == WindowState.Maximized;
        RootGrid.Margin = maximized ? new Thickness(8) : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Restaurer" : "Agrandir";
    }

    /// <summary>Capture de la touche choisie pour le push-to-talk ou un son du soundboard.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel model) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool capturingPushToTalk = model.Microphone.IsCapturingKey;
        bool capturingSwitch = model.Microphone.IsCapturingSwitchKey;
        bool capturingOverlay = model.SettingsPage.IsCapturingOverlayKey;
        var capturingAction = model.Hotkeys.Capturing;
        var capturingSound = model.Soundboard.Sounds.FirstOrDefault(sound => sound.IsCapturingKey);
        if (!capturingPushToTalk && !capturingSwitch && !capturingOverlay &&
            capturingAction == null && capturingSound == null) return;

        e.Handled = true;

        if (key == Key.Escape)
        {
            model.Microphone.IsCapturingKey = false;
            model.Microphone.IsCapturingSwitchKey = false;
            model.SettingsPage.IsCapturingOverlayKey = false;
            if (capturingAction != null) capturingAction.IsCapturing = false;
            if (capturingSound != null) capturingSound.IsCapturingKey = false;
            return;
        }

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        string name = key.ToString();

        if (capturingPushToTalk) model.Microphone.SetPushToTalkKey(virtualKey, name);
        else if (capturingSwitch) model.Microphone.SetSwitchKey(virtualKey, name);
        else if (capturingOverlay) model.SettingsPage.SetOverlayKey(virtualKey, name);
        else if (capturingAction != null) model.Hotkeys.SetKey(capturingAction, virtualKey, name);
        else if (capturingSound != null) model.Soundboard.SetHotkey(capturingSound, virtualKey, name);
    }
}

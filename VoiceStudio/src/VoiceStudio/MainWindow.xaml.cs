using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VoiceStudio.ViewModels;

namespace VoiceStudio;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _meterTimer;

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

        Loaded += (_, _) => _meterTimer.Start();
        Closed += (_, _) => _meterTimer.Stop();
        StateChanged += OnWindowStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
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
        var capturingSound = model.Soundboard.Sounds.FirstOrDefault(sound => sound.IsCapturingKey);
        if (!capturingPushToTalk && capturingSound == null) return;

        e.Handled = true;

        if (key == Key.Escape)
        {
            model.Microphone.IsCapturingKey = false;
            if (capturingSound != null) capturingSound.IsCapturingKey = false;
            return;
        }

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        string name = key.ToString();

        if (capturingPushToTalk) model.Microphone.SetPushToTalkKey(virtualKey, name);
        else if (capturingSound != null) model.Soundboard.SetHotkey(capturingSound, virtualKey, name);
    }
}

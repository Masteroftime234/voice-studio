using System.Windows;
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
    }
}

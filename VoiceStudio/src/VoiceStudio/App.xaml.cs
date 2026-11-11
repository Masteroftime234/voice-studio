using System.Windows;
using VoiceStudio.Audio;
using VoiceStudio.Settings;
using VoiceStudio.ViewModels;

namespace VoiceStudio;

public partial class App : Application
{
    private AudioEngine? _engine;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Voice Studio — erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            _engine = new AudioEngine();
            _viewModel = new MainViewModel(_engine, new SettingsService());
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossible d'initialiser l'audio : " + ex.Message, "Voice Studio",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.SaveSettings();
        _engine?.Dispose();
        base.OnExit(e);
    }
}

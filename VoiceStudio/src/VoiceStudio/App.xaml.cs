using System.Windows;
using Microsoft.Win32;
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
            _viewModel = new MainViewModel(_engine, new SettingsService(), new FilePicker());
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
        _viewModel?.Dispose();
        _engine?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Choix de fichiers audio pour le soundboard.</summary>
    private sealed class FilePicker : IFilePicker
    {
        public IReadOnlyList<string> PickAudioFiles()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choisir des sons",
                Multiselect = true,
                Filter = "Fichiers audio|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac;*.ogg|Tous les fichiers|*.*"
            };
            return dialog.ShowDialog() == true ? dialog.FileNames : [];
        }
    }
}

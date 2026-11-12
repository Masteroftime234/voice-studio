using System.Diagnostics;
using VoiceStudio.Helpers;
using VoiceStudio.Settings;

namespace VoiceStudio.ViewModels;

/// <summary>
/// Configuration guidée du premier lancement : installer le câble virtuel,
/// choisir ses périphériques, régler Discord.
/// </summary>
public sealed class SetupViewModel : ObservableObject
{
    private const string CableUrl = "https://vb-audio.com/Cable/";
    private readonly AppSettings _settings;
    private readonly MixerViewModel _mixer;
    private readonly Action _finished;
    private int _step;

    public SetupViewModel(AppSettings settings, MixerViewModel mixer, Action finished)
    {
        _settings = settings;
        _mixer = mixer;
        _finished = finished;

        NextCommand = new RelayCommand(() => Step++, () => Step < LastStep);
        BackCommand = new RelayCommand(() => Step--, () => Step > 0);
        FinishCommand = new RelayCommand(Finish);
        SkipCommand = new RelayCommand(Finish);
        OpenCableSiteCommand = new RelayCommand(OpenCableSite);
        RecheckCommand = new RelayCommand(() =>
        {
            mixer.RefreshDevices();
            OnPropertyChanged(nameof(CableReady));
            OnPropertyChanged(nameof(CableStatus));
        });
    }

    private const int LastStep = 3;

    public RelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand FinishCommand { get; }
    public RelayCommand SkipCommand { get; }
    public RelayCommand OpenCableSiteCommand { get; }
    public RelayCommand RecheckCommand { get; }

    /// <summary>Étape affichée, de 0 à 3.</summary>
    public int Step
    {
        get => _step;
        private set
        {
            if (!SetProperty(ref _step, Math.Clamp(value, 0, LastStep))) return;
            OnPropertyChanged(nameof(StepLabel));
            OnPropertyChanged(nameof(IsLastStep));
            NextCommand.RaiseCanExecuteChanged();
            BackCommand.RaiseCanExecuteChanged();
            if (_step == 1) RecheckCommand.Execute(null);
        }
    }

    public string StepLabel => $"Étape {Step + 1} sur {LastStep + 1}";

    public bool IsLastStep => Step == LastStep;

    /// <summary>Le câble virtuel est installé et visible par Windows.</summary>
    public bool CableReady => !_mixer.CableMissing;

    public string CableStatus => CableReady
        ? "VB-CABLE est installé : Windows voit bien « CABLE Input »."
        : "VB-CABLE n'est pas encore détecté. Installe-le, redémarre le PC, puis revérifie.";

    private void OpenCableSite()
    {
        try
        {
            Process.Start(new ProcessStartInfo(CableUrl) { UseShellExecute = true });
        }
        catch
        {
            // navigateur indisponible : l'adresse reste affichée dans la fenêtre
        }
    }

    private void Finish()
    {
        _settings.SetupDone = true;
        _finished();
    }
}

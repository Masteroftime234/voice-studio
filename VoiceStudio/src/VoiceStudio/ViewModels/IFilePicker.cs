namespace VoiceStudio.ViewModels;

/// <summary>Ouverture de fichiers, fournie par la fenêtre (l'interface connaît les boîtes de dialogue).</summary>
public interface IFilePicker
{
    /// <summary>Retourne les fichiers audio choisis, vide si l'utilisateur annule.</summary>
    IReadOnlyList<string> PickAudioFiles();
}

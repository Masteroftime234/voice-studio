namespace VoiceStudio.ViewModels;

/// <summary>Ouverture de fichiers, fournie par la fenêtre (l'interface connaît les boîtes de dialogue).</summary>
public interface IFilePicker
{
    /// <summary>Retourne les fichiers audio choisis, vide si l'utilisateur annule.</summary>
    IReadOnlyList<string> PickAudioFiles();

    /// <summary>Fichier de réglages à relire, ou null si l'utilisateur annule.</summary>
    string? PickSettingsFileToOpen();

    /// <summary>Emplacement où écrire une sauvegarde des réglages, ou null si annulé.</summary>
    string? PickSettingsFileToSave(string suggestedName);
}

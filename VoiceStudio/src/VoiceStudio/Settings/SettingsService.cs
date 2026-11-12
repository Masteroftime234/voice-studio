using System.IO;
using System.Text.Json;

namespace VoiceStudio.Settings;

/// <summary>Lecture / écriture des réglages dans %AppData%\VoiceStudio\settings.json.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsService(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoiceStudio", "settings.json");
    }

    /// <summary>Emplacement du fichier de réglages, affiché dans l'application.</summary>
    public string FilePath => _path;

    /// <summary>Copie les réglages actuels dans un fichier choisi par l'utilisateur.</summary>
    public bool Export(AppSettings settings, string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
            return true;
        }
        catch (Exception ex)
        {
            Journal.Write("Sauvegarde des réglages impossible", ex);
            return false;
        }
    }

    /// <summary>Relit des réglages depuis un fichier. Retourne null si le fichier est illisible.</summary>
    public AppSettings? Import(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Journal.Write("Lecture des réglages impossible", ex);
            return null;
        }
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Journal.Write("Réglages illisibles, retour aux valeurs par défaut", ex);
        }
        return new AppSettings();
    }

    /// <summary>Retourne false si l'écriture a échoué.</summary>
    public bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
            return true;
        }
        catch (Exception ex)
        {
            Journal.Write("Enregistrement des réglages impossible", ex);
            return false;
        }
    }
}

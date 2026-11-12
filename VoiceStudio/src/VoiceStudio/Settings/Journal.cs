using System.IO;

namespace VoiceStudio.Settings;

/// <summary>
/// Journal des erreurs, écrit à côté des réglages. Sert à comprendre après coup
/// pourquoi un périphérique a lâché ou pourquoi un son n'a pas voulu se lire.
/// </summary>
public static class Journal
{
    private const int MaximumBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiceStudio", "journal.txt");

    public static void Write(string message, Exception? exception = null)
    {
        string line = exception == null
            ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}"
            : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message} — {exception.GetType().Name}: {exception.Message}";

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                // Le journal ne doit pas grossir sans fin : au-delà d'une certaine taille, on repart.
                if (File.Exists(Path) && new FileInfo(Path).Length > MaximumBytes)
                    File.WriteAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  (journal redémarré){Environment.NewLine}");
                File.AppendAllText(Path, line + Environment.NewLine);
            }
            catch
            {
                // Un journal qui échoue ne doit jamais gêner l'application.
            }
        }
    }
}

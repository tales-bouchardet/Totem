using System.IO;

namespace totem;

/// <summary>
/// Minimal best-effort diagnostic log for failures that would otherwise be
/// swallowed silently (e.g. autosave). Never throws — a logging failure must
/// not affect the app's actual behavior.
/// </summary>
public static class Log
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "aec.totem", "error.log");

    public static void Error(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{context}] {ex}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch { /* logging is itself best-effort */ }
    }
}

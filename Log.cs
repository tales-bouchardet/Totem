using System.IO;

namespace totem;

public static class Log
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "totem", "error.log");

    public static void Error(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{context}] {ex}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch { }
    }
}

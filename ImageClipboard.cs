using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace totem;

/// <summary>
/// Copying images to the clipboard, using WPF's native <see cref="Clipboard"/>.
/// </summary>
public static class ImageClipboard
{
    private static readonly string TempFolder =
        Path.Combine(Path.GetTempPath(), "aec.totem", "clip");

    // File still referenced by a previous "copy as file" (kept alive until the next one).
    private static string? _activeFile;

    /// <summary>Removes temp files left by previous sessions. Call on startup.</summary>
    public static void PurgeLeftovers()
    {
        try
        {
            if (Directory.Exists(TempFolder))
                Directory.Delete(TempFolder, recursive: true);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Copies as an image (bitmap).</summary>
    public static void CopyImage(byte[] imageBytes) => Clipboard.SetImage(Decode(imageBytes));

    /// <summary>Copies as a file (pastes into Explorer, e.g.).</summary>
    public static void CopyAsFile(byte[] imageBytes)
    {
        var path = WriteTemp(imageBytes);
        var files = new System.Collections.Specialized.StringCollection { path };
        Clipboard.SetFileDropList(files);
        SetActiveFile(path);
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static string WriteTemp(byte[] bytes)
    {
        Directory.CreateDirectory(TempFolder);
        var path = Path.Combine(TempFolder, $"img_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void SetActiveFile(string path)
    {
        var previous = _activeFile;
        _activeFile = path;
        if (previous is not null && previous != path)
        {
            try { File.Delete(previous); }
            catch { /* in use by the OS; will be cleaned up on next startup */ }
        }
    }
}

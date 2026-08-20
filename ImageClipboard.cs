using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace totem;

public static class ImageClipboard
{
    private static readonly string TempFolder =
        Path.Combine(Path.GetTempPath(), "totem", "clip");

    private static string? _activeFile;

    public static void PurgeLeftovers()
    {
        try
        {
            if (Directory.Exists(TempFolder))
                Directory.Delete(TempFolder, recursive: true);
        }
        catch { }
    }

    public static void CopyImage(byte[] imageBytes) => Clipboard.SetImage(Decode(imageBytes));

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
            catch { }
        }
    }
}

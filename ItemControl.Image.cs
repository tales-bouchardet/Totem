using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace totem;

// ── image block: paste/pick/copy ──────────────────────────────────────────────
public partial class ItemControl
{
    internal void InputBox_PreviewKeyDown_Paste(object sender, KeyEventArgs e)
    {
        if (!_editing) return;
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
        if (!Clipboard.ContainsImage()) return;

        e.Handled = true;
        try
        {
            var bmp = Clipboard.GetImage();
            if (bmp is null) return;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);

            Model.ImageData = Convert.ToBase64String(ms.ToArray());
            Model.IsImage = true;
            Model.IsCode = false;
            ExitInputEdit();
            UpdateImageSource();
            Changed?.Invoke();
        }
        catch { /* invalid clipboard content — ignore */ }
    }

    private void UpdateImageSource()
    {
        if (Model.ImageData is null) return;
        try
        {
            var bytes = Convert.FromBase64String(Model.ImageData);
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            ImageControl.Source = bmp;
        }
        catch { /* invalid image — ignore */ }
        UpdateInputView();
    }

    private void PickImage()
    {
        var dialog = new OpenFileDialog { Filter = "Imagens|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp" };
        if (dialog.ShowDialog() != true) return;

        Model.ImageData = Convert.ToBase64String(File.ReadAllBytes(dialog.FileName));
        Model.IsImage = true;
        Model.IsCode = false;
        UpdateImageSource();
        Changed?.Invoke();
    }

    private void CopyImageToClipboard()
    {
        if (string.IsNullOrEmpty(Model.ImageData)) return;
        ImageClipboard.CopyImage(Convert.FromBase64String(Model.ImageData));
        ShowCopiedFeedback();
    }

    private void CopyImageAsFile()
    {
        if (string.IsNullOrEmpty(Model.ImageData)) return;
        ImageClipboard.CopyAsFile(Convert.FromBase64String(Model.ImageData));
        ShowCopiedFeedback();
    }

    private void ImageBorder_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => CopyImageToClipboard();

    private void InsertImage_Click(object sender, RoutedEventArgs e) => PickImage();
    private void CopyImage_Click(object sender, RoutedEventArgs e) => CopyImageToClipboard();
    private void CopyImageAsFile_Click(object sender, RoutedEventArgs e) => CopyImageAsFile();

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        Model.IsImage = false;
        Model.ImageData = null;
        ImageControl.Source = null;
        UpdateInputView();
        Changed?.Invoke();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace totem;

/// <summary>
/// A label + input pair. The content is edited as plain text, but displayed
/// rendered as Markdown while not being edited. When clicked, it copies the
/// source text to the clipboard. The label is always visible as a pill (with
/// a "label" placeholder when empty) and editable via a dialog.
///
/// Split into partial files by area:
///   ItemControl.xaml.cs  — core: construction, view-state, copy, label editing.
///   ItemControl.Code.cs  — code block: language switching, gutter, indent.
///   ItemControl.Image.cs — image block: paste/pick/copy.
/// </summary>
public partial class ItemControl : UserControl
{
    // Indent width (Tab) in spaces — used by ItemControl.Code.cs.
    internal const int IndentWidth = 4;

    internal const double PlainFontSize = 16;
    internal const double CodeFontSize = 14;

    // Matches the MinWidth set on the UserControl root in ItemControl.xaml;
    // MainWindow uses it to size the window's own minimum width.
    public const double MinInputWidth = 500;

    private readonly DispatcherTimer _copiedTimer = new() { Interval = TimeSpan.FromMilliseconds(1100) };
    private bool _editing;

    internal static ItemControl? Editing { get; private set; }
    private bool _updatingContentBox; // guards ContentBox_TextChanged during programmatic rebuilds
    private string? _pendingCopyText;

    public TotemItem Model { get; }

    public event Action<ItemControl>? InsertAboveRequested;
    public event Action<ItemControl>? InsertBelowRequested;
    public event Action<ItemControl>? InsertSeparatorBelowRequested;
    public event Action<ItemControl>? DeleteRequested;
    public event Action? Changed;

    public ItemControl(TotemItem model)
    {
        InitializeComponent();
        Model = model;

        if (model.IsSeparator)
        {
            SeparatorVisual.Visibility = Visibility.Visible;
            MainContentGrid.Visibility = Visibility.Collapsed;
            return;
        }

        _copiedTimer.Tick += (_, _) => { _copiedTimer.Stop(); CopiedText.Visibility = Visibility.Collapsed; };
        InputBox.PreviewKeyDown += InputBox_PreviewKeyDown_Paste;
        ContentBox.PreviewKeyDown += InputBox_PreviewKeyDown_Paste;
        ContentBox.PreviewMouseWheel += Content_PreviewMouseWheel;
        CodeReadView.PreviewMouseWheel += Content_PreviewMouseWheel;

        // The gutter has no scrollbar of its own; it just follows whichever
        // content view (edit box or colored reader) is currently scrolling.
        InputBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Content_ScrollChanged));
        CodeReadView.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Content_ScrollChanged));

        // InputBox is the live buffer only while actively editing code, but it's
        // kept populated from the model at all times so a freshly loaded code
        // item renders correctly (RenderCode reads from InputBox.Text).
        InputBox.Text = model.Content;

        if (model.IsImage && model.ImageData is not null)
            UpdateImageSource();

        UpdateLabelDisplay();
        ApplyCodeState(); // triggers UpdateInputView(), which populates ContentBox/CodeReadView
    }

    /// <summary>Makes sure the model reflects whatever is currently being edited.</summary>
    public void Sync()
    {
        if (Model.IsSeparator || !_editing) return;
        Model.Content = Model.IsCode ? InputBox.Text : GetContentBoxText();
    }

    // ── copy ──────────────────────────────────────────────────────────────────

    private void Content_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_editing || string.IsNullOrEmpty(Model.Content)) return;

        var hasSelection = sender switch
        {
            RichTextBox rtb => !rtb.Selection.IsEmpty,
            _ => false,
        };
        if (hasSelection) return; // respect a manual selection instead of copying everything

        Clipboard.SetText(Model.Content);
        ShowCopiedFeedback();
    }

    internal void ShowCopiedFeedback()
    {
        CopiedText.Visibility = Visibility.Visible;
        _copiedTimer.Stop();
        _copiedTimer.Start();
        FlashCopied();
    }

    private void FlashCopied()
    {
        CopyFlash.Opacity = 1;
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        CopyFlash.BeginAnimation(OpacityProperty, anim);
    }

    // ── context menu "Copiar" (selection-aware) ─────────────────────────────

    private void ContentContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        var target = menu.PlacementTarget;
        // Right-click is used constantly, so a bad .Selection read must never take the
        // whole app down with it — worst case the "Copiar" item just doesn't show up.
        try
        {
            _pendingCopyText = string.IsNullOrEmpty(Model.Content) ? null : target switch
            {
                TextBox tb => tb.SelectionLength > 0 ? tb.SelectedText : null,
                RichTextBox rtb => !rtb.Selection.IsEmpty ? rtb.Selection.Text : null,
                _ => null,
            };
        }
        catch { _pendingCopyText = null; }
        var visible = !string.IsNullOrEmpty(_pendingCopyText) ? Visibility.Visible : Visibility.Collapsed;
        // "Copiar" and its separator are always the first two items (see ItemControl.xaml);
        // elements declared inside UserControl.Resources don't get x:Name fields.
        ((MenuItem)menu.Items[0]).Visibility = visible;
        ((Separator)menu.Items[1]).Visibility = visible;
    }

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_pendingCopyText))
            Clipboard.SetText(_pendingCopyText);
    }

    // ── editing ──────────────────────────────────────────────────────────────

    private void EnterInputEdit()
    {
        if (_editing) return;
        _editing = true;
        Editing = this;

        if (Model.IsCode)
        {
            InputBox.IsReadOnly = false;
            UpdateInputView();
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
        }
        else
        {
            SetContentBoxDocument(Model.Content, applyFormatting: false); // raw text while editing
            ContentBox.IsReadOnly = false;
            UpdateInputView();
            ContentBox.Focus();
            ContentBox.CaretPosition = ContentBox.Document.ContentEnd;
        }
    }

    private void ExitInputEdit()
    {
        if (!_editing) return;
        _editing = false;
        if (ReferenceEquals(Editing, this)) Editing = null;

        if (Model.IsCode)
        {
            InputBox.IsReadOnly = true;
            Model.Content = InputBox.Text;
        }
        else
        {
            ContentBox.IsReadOnly = true;
            Model.Content = GetContentBoxText();
        }

        UpdateInputView();
        Changed?.Invoke();
    }

    private void InputBox_LostFocus(object sender, RoutedEventArgs e) => ExitInputEdit();
    private void ContentBox_LostFocus(object sender, RoutedEventArgs e) => ExitInputEdit();

    private void Content_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_editing) return;

        e.Handled = true;
        if (VisualTreeHelper.GetParent((DependencyObject)sender) is not UIElement parent) return;

        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender,
        });
    }

    private void Content_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        GutterScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
        if (ReferenceEquals(sender, InputBox)) SyncCodeScroll();
    }

    private void SyncCodeScroll()
    {
        if (!_editing || !Model.IsCode) return;
        CodeReadView.ScrollToVerticalOffset(InputBox.VerticalOffset);
        CodeReadView.ScrollToHorizontalOffset(InputBox.HorizontalOffset);
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Model.Content = InputBox.Text;
        UpdateEmptyPlaceholder();
        if (Model.IsCode)
        {
            UpdateGutter();
            RenderCode();
        }
        Changed?.Invoke();
    }

    private void ContentBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingContentBox) return;
        Model.Content = GetContentBoxText();
        UpdateEmptyPlaceholder();
        Changed?.Invoke();
    }

    /// <summary>Plain text out of ContentBox's FlowDocument (mirrors InputBox.Text for code).</summary>
    private string GetContentBoxText()
    {
        var text = new TextRange(ContentBox.Document.ContentStart, ContentBox.Document.ContentEnd).Text;
        // TextRange.Text always ends with a trailing "\r\n" for the document's implicit
        // final paragraph mark — strip it so it doesn't accumulate on every edit cycle.
        if (text.EndsWith("\r\n")) text = text.Substring(0, text.Length - 2);
        return text;
    }

    /// <summary>Replaces ContentBox's whole document with a single formatted/plain paragraph.</summary>
    private void SetContentBoxDocument(string text, bool applyFormatting)
    {
        _updatingContentBox = true;
        var paragraph = SimpleMarkdown.BuildParagraph(text, applyFormatting);
        ContentBox.Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
        _updatingContentBox = false;
    }

    /// <summary>Rebuilds ContentBox for read mode: Markdown-formatted, or verbatim for "Texto puro".</summary>
    private void RenderContent() => SetContentBoxDocument(Model.Content, applyFormatting: !Model.IsPlainText);

    private void UpdateEmptyPlaceholder() =>
        InputPlaceholder.Visibility = string.IsNullOrEmpty(Model.Content) && !_editing
            ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Decides which representation is visible: the code editor/reader (InputBox /
    /// CodeReadView) when Model.IsCode, or the shared content box (ContentBox) for
    /// plain text and Markdown — same control for both editing and reading, just
    /// toggling IsReadOnly and rebuilding its document. Image mode wins over all.
    /// </summary>
    private void UpdateInputView()
    {
        var showImage = Model.IsImage;
        var showCodeEdit = !showImage && _editing && Model.IsCode;
        var showCodeRead = !showImage && !_editing && Model.IsCode;
        var showContentEdit = !showImage && _editing && !Model.IsCode;
        var showContentRead = !showImage && !_editing && !Model.IsCode;

        ImageBorder.Visibility = showImage ? Visibility.Visible : Visibility.Collapsed;
        TextAreaBorder.Visibility = showImage ? Visibility.Collapsed : Visibility.Visible;
        InputBox.Visibility = showCodeEdit ? Visibility.Visible : Visibility.Collapsed;
        CodeReadView.Visibility = (showCodeRead || showCodeEdit) ? Visibility.Visible : Visibility.Collapsed;
        CodeReadView.IsHitTestVisible = !showCodeEdit;
        if (showCodeEdit) InputBox.Foreground = Brushes.Transparent;
        else InputBox.ClearValue(ForegroundProperty);
        ContentBox.Visibility = (showContentEdit || showContentRead) ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmptyPlaceholder();

        ContentBox.Focusable = _editing;
        ContentBox.HorizontalScrollBarVisibility =
            _editing ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        ContentBox.VerticalScrollBarVisibility =
            _editing ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
        CodeReadView.Focusable = false;
        CodeReadView.HorizontalScrollBarVisibility =
            showCodeEdit ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
        CodeReadView.VerticalScrollBarVisibility =
            showCodeEdit ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;

        CodeBadge.Visibility = Model.IsCode ? Visibility.Visible : Visibility.Collapsed;
        GutterScrollViewer.Visibility = Model.IsCode && !IsPassword ? Visibility.Visible : Visibility.Collapsed;
        TextAreaBorder.Background = Model.IsCode
            ? (Brush)Resources["CodeBg"]
            : (Model.IsPlainText && !_editing ? (Brush)Resources["PlainTextBg"] : (Brush)Resources["InputBg"]);

        TextAreaBorder.BorderBrush = _editing
            ? (Brush)Resources["AccentBrush"]
            : (Brush)Resources["BorderBrush2"];

        if (showCodeRead || showCodeEdit) RenderCode();
        if (showContentRead) RenderContent();
        UpdateGutter();
    }

    // ── label ────────────────────────────────────────────────────────────────

    private async void EnterLabelEdit()
    {
        var host = App.DialogHost;
        if (host is null) return;

        var input = new TextBox { Text = Model.Label ?? "" };
        var dialog = new Wpf.Ui.Controls.ContentDialog(host)
        {
            Title = "Editar label",
            Content = input,
            PrimaryButtonText = "Salvar",
            CloseButtonText = "Cancelar",
        };

        if (await dialog.ShowAsync() == Wpf.Ui.Controls.ContentDialogResult.Primary)
        {
            var text = input.Text.Trim();
            Model.Label = string.IsNullOrEmpty(text) ? null : text;
            UpdateLabelDisplay();
            Changed?.Invoke();
        }
    }

    private void UpdateLabelDisplay()
    {
        if (Model.Label is null)
        {
            LabelPill.Visibility = Visibility.Collapsed;
        }
        else if (Model.Label.Length == 0)
        {
            LabelPill.Visibility = Visibility.Visible;
            LabelText.Text = "label";
            LabelText.Foreground = (Brush)Resources["PillPlaceholderBrush"];
        }
        else
        {
            LabelPill.Visibility = Visibility.Visible;
            LabelText.Text = Model.Label;
            LabelText.Foreground = (Brush)Resources["PillTextBrush"];
        }
    }

    // ── shared menu handlers ─────────────────────────────────────────────────

    private void InsertAbove_Click(object sender, RoutedEventArgs e) => InsertAboveRequested?.Invoke(this);
    private void InsertBelow_Click(object sender, RoutedEventArgs e) => InsertBelowRequested?.Invoke(this);
    private void InsertSeparatorBelow_Click(object sender, RoutedEventArgs e) => InsertSeparatorBelowRequested?.Invoke(this);
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this);
    private void Edit_Click(object sender, RoutedEventArgs e) => EnterInputEdit();
    private void EditLabel_Click(object sender, RoutedEventArgs e) => EnterLabelEdit();

    private void RemoveLabel_Click(object sender, RoutedEventArgs e)
    {
        Model.Label = null;
        UpdateLabelDisplay();
        Changed?.Invoke();
    }
}

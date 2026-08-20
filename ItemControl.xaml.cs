using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace totem;

public partial class ItemControl : UserControl
{
    internal const int IndentWidth = 4;

    public const double MinInputWidth = 500;

    private readonly DispatcherTimer _copiedTimer = new() { Interval = TimeSpan.FromMilliseconds(1100) };
    private bool _editing;

    internal static ItemControl? Editing { get; private set; }
    private bool _updatingContentBox;
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

        InputBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Content_ScrollChanged));
        CodeReadView.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Content_ScrollChanged));

        InputBox.Text = model.Content;

        if (model.IsImage && model.ImageData is not null)
            UpdateImageSource();

        UpdateLabelDisplay();
        ApplyCodeState();
    }

    public void Sync()
    {
        if (Model.IsSeparator || !_editing) return;
        Model.Content = Model.IsCode ? InputBox.Text : GetContentBoxText();
    }

    private void Content_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_editing || string.IsNullOrEmpty(Model.Content)) return;

        var hasSelection = sender switch
        {
            RichTextBox rtb => !rtb.Selection.IsEmpty,
            _ => false,
        };
        if (hasSelection) return;

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

    private void ContentContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        var target = menu.PlacementTarget;

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

        ((MenuItem)menu.Items[0]).Visibility = visible;
        ((Separator)menu.Items[1]).Visibility = visible;
    }

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_pendingCopyText))
            Clipboard.SetText(_pendingCopyText);
    }

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
            SetContentBoxDocument(Model.Content, applyFormatting: false);
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
            RefreshHighlight();
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

    private string GetContentBoxText()
    {
        var text = new TextRange(ContentBox.Document.ContentStart, ContentBox.Document.ContentEnd).Text;

        if (text.EndsWith("\r\n")) text = text.Substring(0, text.Length - 2);
        return text;
    }

    private void SetContentBoxDocument(string text, bool applyFormatting)
    {
        _updatingContentBox = true;
        var paragraph = SimpleMarkdown.BuildParagraph(text, applyFormatting);
        ContentBox.Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
        _updatingContentBox = false;
    }

    private void RenderContent() => SetContentBoxDocument(Model.Content, applyFormatting: !Model.IsPlainText);

    private void UpdateEmptyPlaceholder() =>
        InputPlaceholder.Visibility = string.IsNullOrEmpty(Model.Content) && !_editing
            ? Visibility.Visible : Visibility.Collapsed;

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
        if (!showCodeEdit)
        {
            StopHighlightDebounce();
            InputBox.ClearValue(ForegroundProperty);
        }
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

        if (showCodeEdit) RefreshHighlight();
        else if (showCodeRead) RenderCode();
        if (showContentRead) RenderContent();
        UpdateGutter();
    }

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

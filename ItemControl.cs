using System.Text;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media.Imaging;
using CommunityToolkit.WinUI.Controls;
using Windows.Security.Cryptography;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.ApplicationModel.DataTransfer;

namespace totem;

/// <summary>
/// Um par label + input. O conteúdo é editado e copiado como texto puro, mas
/// exibido renderizado em Markdown enquanto não está em edição. Ao ser clicado,
/// copia o texto-fonte para a área de transferência. A label é sempre visível como
/// pílula (com placeholder "label" quando vazia) e editável via popup de diálogo.
/// </summary>
public sealed class ItemControl : Grid
{
    // Largura responsiva: cresce com a janela, limitada a [500, 900] px.
    public const double MinInputWidth = 500;
    private const double MaxInputWidth = 900;

    // Altura máxima de qualquer bloco: acima disso, rola verticalmente dentro do bloco.
    private const double BlockMaxHeight = 300;

    private readonly Border _labelPill = null!;
    private readonly TextBlock _labelText = null!;
    private readonly TextBox _input = null!;          // edição/cópia em texto puro
    private readonly MarkdownTextBlock _md = null!;    // exibição renderizada em Markdown
    private readonly Border _mdBorder = null!;
    private readonly RichTextBlock _codeText = null!;  // código com realce de sintaxe (leitura)
    private readonly Border _codeBorder = null!;
    private readonly TextBlock _codeLangBadge = null!;
    private readonly TextBlock _codeGutter = null!;    // numeração de linhas (leitura)
    private readonly RichEditBox _codeEdit = null!;    // edição de código com realce ao vivo
    private readonly Border _codeEditBorder = null!;
    private readonly TextBlock _codeEditGutter = null!; // numeração de linhas (edição)
    private readonly TextBlock _codeEditBadge = null!;  // mesma "badge" de linguagem na edição
    private readonly ScrollViewer _codeEditGutterScroll = null!; // calha do editor (sincronizada)
    private readonly Image _imageControl = null!;      // bloco de imagem
    private readonly Border _imageBorder = null!;
    private readonly TextBlock _placeholder = null!;
    private readonly TextBlock _copied = null!;
    private readonly DispatcherTimer _copiedTimer = null!;
    private readonly Border _copyFlash = null!;        // borda que pisca na cor de destaque ao copiar
    private readonly Storyboard _copyFlashSb = null!;
    private RichTextBlock? _mdRichText;       // RichTextBlock interno do Markdown
    private bool _editing;
    private bool _codeEditScrollHooked; // calha do editor já sincronizada ao scroll interno
    private int _lastSelEnd     = -1;   // detecta ponta ativa na seleção do editor (edição)
    private int _lastReadSelEnd = -1;   // detecta ponta ativa na seleção do leitor (leitura)
    private bool _loadingCode;      // suprime sincronização durante atribuições programáticas
    private bool _highlightingCode; // guarda contra reentrância ao recolorir o editor
    // Último texto realçado: o RichEditBox dispara TextChanged também ao mudar a
    // FORMATAÇÃO; comparar o texto evita um laço infinito de recoloração.
    private string _lastCodeText = string.Empty;
    private string _lastHighlightedText = string.Empty; // texto na última recoloração (p/ diff)
    private DispatcherTimer? _highlightTimer; // adia a recoloração até a digitação parar
    private int _lastGutterLines = -1;        // evita reconstruir a calha sem necessidade

    // Largura do recuo (Tab) em espaços.
    private const int IndentWidth = 4;

    // Cor padrão do texto dentro do bloco de código (fundo escuro fixo).
    private static readonly Windows.UI.Color CodeDefaultColor = Windows.UI.Color.FromArgb(255, 0xFF, 0xFF, 0xFF);

    public TotemItem Model { get; }

    public event Action<ItemControl>? InsertAboveRequested;
    public event Action<ItemControl>? InsertBelowRequested;
    public event Action<ItemControl>? InsertSeparatorBelowRequested;
    public event Action<ItemControl>? DeleteRequested;
    public event Action? Changed;

    // Cor base das labels (tags) — roxo #6500f9 puxado um pouco para o azul.
    public static readonly Windows.UI.Color AccentColor = Windows.UI.Color.FromArgb(255, 0x4E, 0x2A, 0xCC);

    // Tag em tonalidades roxo-azulado: fundo escuro opaco, borda viva e texto
    // lavanda claro. Opaco (não translúcido) para sobrepor o input limpo.
    private static readonly SolidColorBrush PillBrush = new(Windows.UI.Color.FromArgb(255, 0x33, 0x26, 0x78));
    private static readonly SolidColorBrush PillBorderBrush = new(Windows.UI.Color.FromArgb(255, 0x4E, 0x2A, 0xCC));
    private static readonly SolidColorBrush PillTextBrush = new(Windows.UI.Color.FromArgb(255, 0xF2, 0xF0, 0xFF));
    private static readonly SolidColorBrush PillPlaceholderBrush = new(Windows.UI.Color.FromArgb(140, 0xF2, 0xF0, 0xFF));
    private static readonly SolidColorBrush InputBg     = new(Windows.UI.Color.FromArgb(13,  255, 255, 255)); // fundo padrão do input
    private static readonly SolidColorBrush PlainTextBg = new(Windows.UI.Color.FromArgb(30,  255, 255, 255)); // fundo texto puro (levemente mais escuro)

    // O TextBox do WinUI guarda quebras como '\r'. Ao atribuir Text por código com
    // '\r' isolado, ele TRUNCA na 1ª quebra — então o conteúdo após a quebra de
    // linha se perdia ao recarregar (e o save seguinte gravava a versão truncada).
    // Atribuir com '\r\n' preserva todas as linhas.
    private static string ToEditorText(string s) =>
        string.IsNullOrEmpty(s) ? s
            : s.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

    public ItemControl(TotemItem model)
    {
        Model = model;
        Background = new SolidColorBrush(Colors.Transparent); // garante hit-test em toda a área

        // Coluna responsiva centralizada: estica com a janela até 900px e não encolhe
        // abaixo de 600px (Stretch + MaxWidth centraliza o bloco quando há folga).
        HorizontalAlignment = HorizontalAlignment.Stretch;
        MinWidth = MinInputWidth;
        MaxWidth = MaxInputWidth;

        if (model.IsSeparator)
        {
            Children.Add(new Border
            {
                Height = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(45, 255, 255, 255)),
                Margin = new Thickness(50, 14, 50, 14),
            });
            ContextFlyout = BuildSeparatorMenu();
            return;
        }

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── label: pílula sempre visível (placeholder quando vazia) ───────────
        _labelText = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 30, // tracking sutil para um ar mais "tag"
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(13, 2, 13, 2),
        };
        _labelPill = new Border
        {
            Background = PillBrush,
            BorderBrush = PillBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            MinWidth = 48,
            HorizontalAlignment = HorizontalAlignment.Left,
            // Recuo à esquerda; margem inferior negativa faz a tag descer um pouco
            // e sobrepor o topo do input.
            Margin = new Thickness(14, 0, 0, -7),
            Child = _labelText,
        };
        SetRow(_labelPill, 0);
        Children.Add(_labelPill);
        // Garante que a tag fique por cima do input (que é adicionado depois).
        Canvas.SetZIndex(_labelPill, 1);

        // ── input (somente-leitura -> copia ao clicar) ────────────────────────
        _input = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsSpellCheckEnabled = false,
            Padding = new Thickness(16, 10, 16, 10),
            MaxHeight = BlockMaxHeight, // rola verticalmente quando passa de 300px
            // Text por ÚLTIMO: se for atribuído antes de AcceptsReturn=true, o
            // TextBox está em modo linha-única e trunca na primeira quebra.
            Text = ToEditorText(model.Content),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_input, ScrollBarVisibility.Auto);

        _input.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnInputPointerReleased), handledEventsToo: true);
        _input.AddHandler(UIElement.KeyDownEvent,
            new KeyEventHandler(OnInputKeyDown), handledEventsToo: true);

        // Placeholder personalizado: sobreposto ao input, controlamos a opacidade.
        _placeholder = new TextBlock
        {
            Text = "(vazio — clique direito › Editar)",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(204, 140, 140, 140)), // atualizado em ApplyThemeBrushes
            Padding = new Thickness(19, 11, 16, 10),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Top,
        };

        // ── exibição renderizada em Markdown (modo leitura) ───────────────────
        // Criado antes de conectar o TextChanged do input, que escreve em _md.
        _md = new MarkdownTextBlock
        {
            Text = _input.Text,
            Config = new MarkdownConfig(),
            IsTextSelectionEnabled = true,  // permite selecionar trechos do texto
            DisableLinks = true,            // mantém o gesto "clicar para copiar"
            // \n simples vira quebra de linha (caso contrário o Markdown junta
            // tudo num parágrafo só e o texto vira "single line" na exibição).
            UseSoftlineBreakAsHardlineBreak = true,
            UseEmphasisExtras = true,       // ~~tachado~~, super/subscrito
            UseListExtras = true,
            UseAutoLinks = true,
            UsePipeTables = true,
            UseTaskLists = true,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        // A seleção é feita no RichTextBlock interno; quando ele entra na árvore
        // visual trocamos só o menu de contexto (clique direito) pelo personalizado,
        // mantendo a seleção de trechos funcionando.
        _md.Loaded += (_, _) => ConfigureMarkdownContextMenu();
        var mdScroll = new ScrollViewer
        {
            Content = _md,
            MaxHeight = BlockMaxHeight, // rola verticalmente quando passa de 300px
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        _mdBorder = new Border
        {
            Child = mdScroll,
            Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["TextControlElevationBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 10, 16, 10),
        };
        // O MarkdownTextBlock consome eventos de ponteiro nos blocos internos;
        // registramos com handledEventsToo para copiar mesmo assim.
        _mdBorder.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnInputPointerReleased), handledEventsToo: true);

        // ── bloco de código: leitura e edição compartilham aparência ──────────
        // Fonte, cores e layout (calha de numeração + badge da linguagem) são iguais
        // nos dois modos, para que entrar/sair da edição não "salte" visualmente.
        var codeFont = new FontFamily("Cascadia Mono, Consolas, Courier New");
        var codeBg = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x14, 0x14, 0x14));
        var gutterFg = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 0xFF, 0xFF, 0xFF));
        var gutterLine = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 0xFF, 0xFF, 0xFF));

        // ── exibição de código com realce de sintaxe (modo leitura) ───────────
        _codeText = new RichTextBlock
        {
            FontFamily = codeFont,
            FontSize = 13,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.NoWrap,
            Foreground = new SolidColorBrush(CodeDefaultColor),
        };
        var codeMenu = BuildInputMenu();
        codeMenu.Opening += (s, _) =>
        {
            var menu = (MenuFlyout)s!;
            menu.Items.Clear();
            PopulateInputMenu(menu, _codeText.SelectedText);
        };
        _codeText.ContextFlyout = codeMenu;
        // Scroller único do bloco: viewport de 300px, então as barras (vertical à direita,
        // horizontal embaixo) ficam nas bordas visíveis e dá pra arrastar; a roda rola
        // verticalmente normalmente.
        var codeScroll = new ScrollViewer
        {
            Content = _codeText,
            MaxHeight = BlockMaxHeight,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        _codeGutter = MakeGutter(codeFont, gutterFg);
        var codeGutterScroll = MakeGutterScroller(_codeGutter);
        // a calha acompanha a rolagem vertical do código (presa na horizontal)
        codeScroll.ViewChanged += (_, _) =>
            codeGutterScroll.ChangeView(null, codeScroll.VerticalOffset, null, true);
        _codeText.SelectionChanged += (_, _) =>
        {
            var endPtr   = _codeText.SelectionEnd;
            var startPtr = _codeText.SelectionStart;
            if (endPtr is null || startPtr is null) return;
            var endOff = endPtr.Offset;
            if (startPtr.Offset == endOff) return; // seleção colapsada
            // Ponta ativa = a que mudou desde a última notificação
            var rect = (endOff != _lastReadSelEnd)
                ? endPtr.GetCharacterRect(LogicalDirection.Backward)
                : startPtr.GetCharacterRect(LogicalDirection.Forward);
            _lastReadSelEnd = endOff;
            const double pad = 8.0;
            var vl = codeScroll.HorizontalOffset;
            var vr = vl + codeScroll.ViewportWidth;
            if (rect.X < vl + pad)
                codeScroll.ChangeView(Math.Max(0, rect.X - pad), null, null, false);
            else if (rect.Right > vr - pad)
                codeScroll.ChangeView(rect.Right - codeScroll.ViewportWidth + pad, null, null, false);
        };
        _codeLangBadge = MakeLangBadge(CodeLanguages.ById(model.Language)?.Name ?? model.Language ?? "");
        var codeGrid = MakeCodeGrid(_codeLangBadge, codeGutterScroll, gutterLine, codeScroll);

        _codeBorder = new Border
        {
            Child = codeGrid,
            Background = codeBg,
            BorderBrush = (Brush)Application.Current.Resources["TextControlElevationBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 10, 16, 10),
            Visibility = Visibility.Collapsed,
        };
        _codeBorder.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnInputPointerReleased), handledEventsToo: true);

        // ── edição de código com realce ao vivo (RichEditBox) ─────────────────
        // Tema escuro (cursor/seleção claros), sem moldura nem sublinhado de foco e
        // fundo transparente para herdar o mesmo visual do modo leitura. Cresce com o
        // conteúdo (scroll vertical desligado) para alinhar com a calha de numeração.
        _codeEdit = new RichEditBox
        {
            FontFamily = codeFont,
            FontSize = 13,
            AcceptsReturn = true,
            IsSpellCheckEnabled = false,
            TextWrapping = TextWrapping.NoWrap,
            RequestedTheme = ElementTheme.Dark,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MaxHeight = BlockMaxHeight, // viewport de 300px: rola internamente e segue o cursor
            Foreground = new SolidColorBrush(CodeDefaultColor),
            // Sem negrito/itálico via teclado: só aplicamos cor, não estilo de fonte.
            DisabledFormattingAccelerators = DisabledFormattingAccelerators.All,
        };
        // Remove o chrome de caixa de texto (fundo de estados e sublinhado de foco).
        var transparent = new SolidColorBrush(Colors.Transparent);
        _codeEdit.Resources["TextControlBackground"] = transparent;
        _codeEdit.Resources["TextControlBackgroundPointerOver"] = transparent;
        _codeEdit.Resources["TextControlBackgroundFocused"] = transparent;
        _codeEdit.Resources["TextControlBackgroundDisabled"] = transparent;
        _codeEdit.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        _codeEdit.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        // O próprio RichEditBox rola (vertical e horizontal) dentro dos 300px, com barras
        // visíveis nas bordas e seguindo o cursor ao digitar.
        ScrollViewer.SetVerticalScrollMode(_codeEdit, ScrollMode.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_codeEdit, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_codeEdit, ScrollBarVisibility.Hidden);
        _codeEdit.TextChanged += OnCodeEditTextChanged;
        _codeEdit.SelectionChanged += (_, _) =>
        {
            var sel = _codeEdit.Document.Selection;
            var end = sel.EndPosition;
            var start = sel.StartPosition;
            // Detecta a ponta ativa (a que mudou) para rolar até ela.
            var activePos = (end != _lastSelEnd) ? end : start;
            _lastSelEnd = end;
            if (start == end) return; // cursor sem seleção: editor já segue nativamente
            _codeEdit.Document.GetRange(activePos, activePos)
                               .ScrollIntoView(PointOptions.None);
        };
        _codeEdit.LostFocus += (_, _) => ExitInputEdit();
        _codeEdit.Paste += OnCodeEditPaste; // cola como texto puro (sem formatação herdada)
        _codeEdit.AddHandler(UIElement.KeyDownEvent,
            new KeyEventHandler(OnCodeEditKeyDown), handledEventsToo: true);

        _codeEditGutter = MakeGutter(codeFont, gutterFg);
        _codeEditGutterScroll = MakeGutterScroller(_codeEditGutter);
        // A calha do editor acompanha a rolagem vertical interna do RichEditBox. O
        // ScrollViewer interno só existe depois que o editor é realizado (visível); por
        // isso tentamos enganchar a cada passo de layout até conseguir (e então paramos).
        _codeEdit.LayoutUpdated += OnCodeEditLayoutUpdated;
        _codeEditBadge = MakeLangBadge("");
        var codeEditGrid = MakeCodeGrid(_codeEditBadge, _codeEditGutterScroll, gutterLine, _codeEdit);

        _codeEditBorder = new Border
        {
            Child = codeEditGrid,
            Background = codeBg,
            BorderBrush = (Brush)Application.Current.Resources["TextControlElevationBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 10, 16, 10),
            Visibility = Visibility.Collapsed,
        };

        // ── bloco de imagem ───────────────────────────────────────────────────
        _imageControl = new Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            MaxHeight = 200,
            MaxWidth = 400,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false, // deixa os cliques chegarem ao _imageBorder
        };
        _imageBorder = new Border
        {
            Child = _imageControl,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(13, 255, 255, 255)),
            BorderBrush = (Brush)Application.Current.Resources["TextControlElevationBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Visibility = Visibility.Collapsed,
        };
        _imageBorder.AddHandler(UIElement.TappedEvent,
            new TappedEventHandler(async (_, _) => await CopyImageToClipboardAsync()),
            handledEventsToo: true);
        _imageBorder.ContextFlyout = BuildImageMenu();

        _input.TextChanged += (_, _) =>
        {
            Model.Content = _input.Text;
            _md.Text = _input.Text;
            _placeholder.Visibility = string.IsNullOrEmpty(_input.Text) && !_editing
                ? Visibility.Visible : Visibility.Collapsed;
            Changed?.Invoke();
        };
        _input.LostFocus += (_, _) => ExitInputEdit();

        SetRow(_input, 1);
        Children.Add(_input);
        SetRow(_placeholder, 1);
        Children.Add(_placeholder);
        SetRow(_mdBorder, 1);
        Children.Add(_mdBorder);
        SetRow(_codeBorder, 1);
        Children.Add(_codeBorder);
        SetRow(_codeEditBorder, 1);
        Children.Add(_codeEditBorder);
        SetRow(_imageBorder, 1);
        Children.Add(_imageBorder);

        // ── feedback "Copiado!" ───────────────────────────────────────────────
        _copied = new TextBlock
        {
            Text = "Copiado!",
            FontSize = 11,
            Foreground = PillTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 8, 6),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        SetRow(_copied, 1);
        Children.Add(_copied);

        _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
        _copiedTimer.Tick += (_, _) => { _copiedTimer.Stop(); _copied.Visibility = Visibility.Collapsed; };

        // ── flash da borda ao copiar (borda na cor de destaque com easing) ────
        _copyFlash = new Border
        {
            BorderBrush = new SolidColorBrush(AccentColor),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Colors.Transparent),
            IsHitTestVisible = false,
            Opacity = 0,
        };
        SetRow(_copyFlash, 1);
        Children.Add(_copyFlash);

        var flash = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(700)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(flash, _copyFlash);
        Storyboard.SetTargetProperty(flash, "Opacity");
        _copyFlashSb = new Storyboard { Children = { flash } };

        ContextFlyout = BuildInputMenu();
        // O TextBox nativo mostra o TextCommandBarFlyout ("Selecionar Tudo", Copiar…)
        // tanto no clique direito (ContextFlyout) quanto ao selecionar (SelectionFlyout).
        // Substituímos o menu de contexto pelo nosso e desligamos o de seleção, para
        // que qualquer clique direito sobre o input abra o menu personalizado.
        var inputMenu = BuildInputMenu();
        inputMenu.Opening += (s, _) =>
        {
            var menu = (MenuFlyout)s!;
            menu.Items.Clear();
            PopulateInputMenu(menu, _input.SelectedText);
        };
        _input.ContextFlyout = inputMenu;
        _input.SelectionFlyout = null;
        _mdBorder.ContextFlyout = BuildInputMenu();
        _labelPill.ContextFlyout = BuildLabelMenu();

        if (model.IsImage && model.ImageData is not null)
            _ = UpdateImageSourceAsync();

        UpdateLabelDisplay();
        ApplyCodeState();

        // Os brushes dependem de recursos de tema que só ficam disponíveis após
        // o controle entrar na árvore visual, por isso são aplicados no Loaded.
        Loaded += (_, _) => ApplyThemeBrushes();
    }

    /// <summary>Reaplica os brushes dependentes de tema (fundo/borda das caixas).</summary>
    private void ApplyThemeBrushes()
    {
        _input.Background = (Model.IsPlainText && !_editing) ? PlainTextBg : InputBg;
        _mdBorder.Background = InputBg;

        _placeholder.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(204, 140, 140, 140));

        // Substitui a cor de foco do sistema pelo roxo das labels.
        _input.Resources["TextControlBorderBrushFocused"]             = PillBorderBrush;
        _input.Resources["TextControlBorderBrushPointerOver"]         = PillBorderBrush;

        var borderBrush = (Brush)Application.Current.Resources["TextControlElevationBorderBrush"];
        _mdBorder.BorderBrush       = borderBrush;
        _codeBorder.BorderBrush     = borderBrush;
        _codeEditBorder.BorderBrush = borderBrush;
    }

    /// <summary>Garante que o modelo reflita o conteúdo em edição.</summary>
    public void Sync()
    {
        if (Model.IsSeparator) return;
        // Ao editar código, a fonte de verdade é o RichEditBox (não espelhamos em
        // _input a cada tecla, por desempenho); lê-se direto dele ao salvar.
        if (_editing && Model.IsCode)
        {
            _codeEdit.Document.GetText(TextGetOptions.None, out var text);
            // GetText(None) sempre acrescenta '\r' implícito no final — removemos
            // para não acumular uma linha em branco a cada ciclo de autosave/edição.
            if (text.Length > 0 && text[^1] == '\r')
                text = text[..^1];
            Model.Content = ToEditorText(text);
        }
        else
        {
            Model.Content = _input.Text;
        }
        // Model.Label é gerenciado diretamente pelo diálogo de edição
    }

    // ── bloco de imagem ──────────────────────────────────────────────────────────

    private async void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_editing) return;
        if (e.Key != Windows.System.VirtualKey.V) return;

        var ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        if ((ctrl & Windows.UI.Core.CoreVirtualKeyStates.Down) == 0) return;

        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Bitmap)) return;

        e.Handled = true;
        try
        {
            var streamRef = await content.GetBitmapAsync();
            using var stream = await streamRef.OpenReadAsync();
            var tamanho = (uint)stream.Size;
            var reader = new DataReader(stream);
            await reader.LoadAsync(tamanho);
            var bytes = new byte[tamanho];
            reader.ReadBytes(bytes);

            Model.ImageData = Convert.ToBase64String(bytes);
            Model.IsImage = true;
            Model.IsCode = false;
            ExitInputEdit();
            await UpdateImageSourceAsync();
            Changed?.Invoke();
        }
        catch { /* clipboard inválido — ignora */ }
    }

    private async Task UpdateImageSourceAsync()
    {
        if (Model.ImageData is null) return;
        try
        {
            var bytes = Convert.FromBase64String(Model.ImageData);
            using var ms = new InMemoryRandomAccessStream();
            using var writer = new DataWriter(ms);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            ms.Seek(0);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(ms);
            _imageControl.Source = bmp;
        }
        catch { /* imagem inválida — ignora */ }
        UpdateInputView();
    }

    private async Task PickImageAsync()
    {
        if (App.MainWindow is null) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" })
            picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

        var arquivo = await picker.PickSingleFileAsync();
        if (arquivo is null) return;

        var buffer = await Windows.Storage.FileIO.ReadBufferAsync(arquivo);
        CryptographicBuffer.CopyToByteArray(buffer, out var dados);

        Model.ImageData = Convert.ToBase64String(dados);
        Model.IsImage = true;
        Model.IsCode = false;
        await UpdateImageSourceAsync();
        Changed?.Invoke();
    }

    private async Task CopyImageToClipboardAsync()
    {
        if (string.IsNullOrEmpty(Model.ImageData)) return;
        await ImageClipboard.CopyImageAsync(Convert.FromBase64String(Model.ImageData));
        ShowCopiedFeedback();
    }

    private async Task CopyImageAsFileAsync()
    {
        if (string.IsNullOrEmpty(Model.ImageData)) return;
        await ImageClipboard.CopyAsFileAsync(Convert.FromBase64String(Model.ImageData));
        ShowCopiedFeedback();
    }

    private void ShowCopiedFeedback()
    {
        _copied.Visibility = Visibility.Visible;
        _copiedTimer.Stop();
        _copiedTimer.Start();
        FlashCopied();
    }

    private MenuFlyout BuildImageMenu()
    {
        var menu = new MenuFlyout();
        // Win+V só guarda bitmaps: "Copiar imagem" entra no histórico; "Copiar como
        // arquivo" cola no Explorer (mas fica fora do histórico).
        menu.Items.Add(MakeMenuItem("Copiar imagem", "", async () => await CopyImageToClipboardAsync()));
        menu.Items.Add(MakeMenuItem("Copiar como arquivo", "", async () => await CopyImageAsFileAsync()));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MakeMenuItem("Trocar imagem", "", async () => await PickImageAsync()));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MakeMenuItem("Inserir input acima",  "", () => InsertAboveRequested?.Invoke(this)));
        menu.Items.Add(MakeMenuItem("Inserir input abaixo", "", () => InsertBelowRequested?.Invoke(this)));
        menu.Items.Add(MakeMenuItem("Adicionar separador abaixo", "", () => InsertSeparatorBelowRequested?.Invoke(this)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MakeMenuItem("Adicionar label", "", EnterLabelEdit));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MakeMenuItem("Remover imagem", "", () =>
        {
            Model.IsImage = false;
            Model.ImageData = null;
            _imageControl.Source = null;
            UpdateInputView();
            Changed?.Invoke();
        }));
        menu.Items.Add(MakeMenuItem("Excluir", "", () => DeleteRequested?.Invoke(this)));
        return menu;
    }

    // ── copiar ─────────────────────────────────────────────────────────────────

    private void OnInputPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_editing || string.IsNullOrEmpty(_input.Text)) return;

        var kind = e.GetCurrentPoint(_input).Properties.PointerUpdateKind;
        if (kind is PointerUpdateKind.RightButtonReleased or PointerUpdateKind.MiddleButtonReleased)
            return; // direita = menu de contexto

        // Se o usuário selecionou um trecho (no Markdown ou no código), respeita a
        // seleção em vez de copiar o conteúdo inteiro (copia só o que ele marcou).
        if (_mdRichText is { SelectedText.Length: > 0 } ||
            _codeText is { SelectedText.Length: > 0 })
            return;

        var pkg = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        pkg.SetText(ToEditorText(_input.Text));
        Clipboard.SetContent(pkg);

        _copied.Visibility = Visibility.Visible;
        _copiedTimer.Stop();
        _copiedTimer.Start();
        FlashCopied();
    }

    /// <summary>Pisca a borda na cor de destaque (com easing) para sinalizar a cópia.</summary>
    private void FlashCopied()
    {
        _copyFlash.Opacity = 1;
        _copyFlashSb.Begin();
    }

    // ── editar input ─────────────────────────────────────────────────────────

    private void EnterInputEdit()
    {
        if (_editing) return;
        _editing = true;

        if (Model.IsCode)
        {
            LoadCodeEditor();
            UpdateInputView();
            // foca após o flyout fechar; cursor ao fim do texto. O editor já está visível
            // aqui, então o ScrollViewer interno existe para sincronizar a calha.
            DispatcherQueue.TryEnqueue(() =>
            {
                HookCodeEditScroll();
                _codeEdit.Document.Selection.SetRange(TextConstants.MaxUnitCount, TextConstants.MaxUnitCount);
                _codeEdit.Focus(FocusState.Programmatic);
            });
        }
        else
        {
            _input.IsReadOnly = false;
            UpdateInputView();
            // foca após o flyout fechar
            DispatcherQueue.TryEnqueue(() => _input.Focus(FocusState.Programmatic));
        }
    }

    private void ExitInputEdit()
    {
        if (!_editing) return;
        _editing = false;
        _highlightTimer?.Stop(); // cancela recoloração pendente (a leitura re-renderiza)
        if (Model.IsCode)
        {
            // garante que _input reflita a última edição do RichEditBox.
            // GetText(None) sempre inclui '\r' implícito no final — removemos antes de
            // salvar para não acumular uma linha em branco a cada ciclo de edição.
            _codeEdit.Document.GetText(TextGetOptions.None, out var codeText);
            if (codeText.Length > 0 && codeText[^1] == '\r')
                codeText = codeText[..^1];
            SyncCodeToInput(codeText);
        }
        _input.IsReadOnly = true;
        Model.Content = _input.Text;
        UpdateInputView();
        Changed?.Invoke();
    }

    /// <summary>
    /// Decide qual representação fica visível: o editor de código com realce ao vivo
    /// (ao editar um bloco de código), a caixa de texto comum (ao editar texto/Markdown
    /// ou quando vazio), o código com realce (código em leitura) ou o Markdown
    /// renderizado (texto simples em leitura).
    /// </summary>
    private void UpdateInputView()
    {
        var showImage    = Model.IsImage;
        var showCodeEdit = !showImage && _editing && Model.IsCode;
        // Texto puro e edição usam o mesmo _input; vazio também o mostra (placeholder).
        var showEditor   = !showImage && !showCodeEdit && (_editing || string.IsNullOrEmpty(_input.Text) || Model.IsPlainText);
        var showCode     = !showImage && !showCodeEdit && !showEditor && Model.IsCode;
        var showMarkdown = !showImage && !showCodeEdit && !showEditor && !Model.IsCode;

        _imageBorder.Visibility    = showImage    ? Visibility.Visible : Visibility.Collapsed;
        _codeEditBorder.Visibility = showCodeEdit ? Visibility.Visible : Visibility.Collapsed;
        _input.Visibility          = showEditor   ? Visibility.Visible : Visibility.Collapsed;
        _codeBorder.Visibility     = showCode     ? Visibility.Visible : Visibility.Collapsed;
        _mdBorder.Visibility       = showMarkdown ? Visibility.Visible : Visibility.Collapsed;
        _placeholder.Visibility    = showEditor && string.IsNullOrEmpty(_input.Text) && !_editing
            ? Visibility.Visible : Visibility.Collapsed;

        // Fundo levemente mais escuro no modo texto puro (leitura); normal ao editar.
        _input.Background = (Model.IsPlainText && !_editing) ? PlainTextBg : InputBg;

        if (showCode) RenderCode();
    }

    private void RenderCode()
    {
        UpdateCodeBadge();
        UpdateCodeGutter();
        _codeText.Blocks.Clear();
        _codeText.Blocks.Add(CodeHighlighter.BuildParagraph(_input.Text, Model.Language));
    }

    // ── editar label (popup, mesmo padrão do rename de abas) ──────────────────

    private async void EnterLabelEdit()
    {
        if (XamlRoot is null) return;

        var currentText = Model.Label ?? "";
        var input = new TextBox
        {
            Text = currentText,
            PlaceholderText = "label",
            SelectionStart = 0,
        };
        var dialog = new ContentDialog
        {
            Title = "Editar label",
            Content = input,
            PrimaryButtonText = "Salvar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
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
            _labelPill.Visibility = Visibility.Collapsed;
        }
        else if (Model.Label.Length == 0)
        {
            _labelPill.Visibility = Visibility.Visible;
            _labelText.Text = "label";
            _labelText.Foreground = PillPlaceholderBrush;
        }
        else
        {
            _labelPill.Visibility = Visibility.Visible;
            _labelText.Text = Model.Label;
            _labelText.Foreground = PillTextBrush;
        }
    }

    // ── bloco de código ──────────────────────────────────────────────────────

    private void SetCode(CodeLanguage lang)
    {
        Model.IsCode = true;
        Model.IsPlainText = false;
        Model.Language = lang.Id;
        if (string.IsNullOrWhiteSpace(_input.Text))
            _input.Text = lang.Skeleton;
        ApplyCodeState();
        Changed?.Invoke();
    }

    private void SetPlainText()
    {
        Model.IsCode = false;
        Model.IsPlainText = true;
        Model.Language = null;
        ApplyCodeState();
        Changed?.Invoke();
    }

    private void SetPlain()
    {
        Model.IsCode = false;
        Model.IsPlainText = false;
        Model.Language = null;
        ApplyCodeState();
        Changed?.Invoke();
    }

    private void ApplyCodeState()
    {
        if (Model.IsCode)
        {
            _input.FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New");
            _input.TextWrapping = TextWrapping.NoWrap;
            ScrollViewer.SetHorizontalScrollBarVisibility(_input, ScrollBarVisibility.Auto);
        }
        else
        {
            _input.ClearValue(Control.FontFamilyProperty);
            _input.TextWrapping = TextWrapping.Wrap;
            ScrollViewer.SetHorizontalScrollBarVisibility(_input, ScrollBarVisibility.Disabled);
        }

        UpdateInputView();

        // Se a troca para código aconteceu durante a edição (ex.: a partir da caixa de
        // texto comum), passa a editar no RichEditBox com realce ao vivo.
        if (_editing && Model.IsCode)
        {
            LoadCodeEditor();
            DispatcherQueue.TryEnqueue(() => _codeEdit.Focus(FocusState.Programmatic));
        }
    }

    // ── bloco de código: editor com realce ao vivo ───────────────────────────

    /// <summary>
    /// Liga a calha do editor à rolagem vertical interna do RichEditBox (uma única vez).
    /// O ScrollViewer interno só existe depois que o controle é realizado, por isso isto
    /// é chamado tanto no Loaded quanto ao entrar na edição.
    /// </summary>
    private void OnCodeEditLayoutUpdated(object? sender, object e)
    {
        HookCodeEditScroll();
        if (_codeEditScrollHooked)
            _codeEdit.LayoutUpdated -= OnCodeEditLayoutUpdated; // já enganchado: para de tentar
    }

    private void HookCodeEditScroll()
    {
        if (_codeEditScrollHooked) return;
        if (FindDescendant<ScrollViewer>(_codeEdit) is not { } sv) return;
        sv.ViewChanged += (_, _) =>
            _codeEditGutterScroll.ChangeView(null, sv.VerticalOffset, null, true);
        _codeEditScrollHooked = true;
    }

    /// <summary>Carrega o texto atual no RichEditBox e aplica o realce inicial.</summary>
    private void LoadCodeEditor()
    {
        _loadingCode = true;
        // RichEditBox usa '\r' como quebra de linha.
        var text = _input.Text.Replace("\r\n", "\r").Replace('\n', '\r');
        _codeEdit.Document.SetText(TextSetOptions.None, text);
        _loadingCode = false;
        _codeEdit.Document.GetText(TextGetOptions.None, out var actual);
        _lastCodeText = actual;
        _lastHighlightedText = string.Empty; // força recoloração completa ao abrir
        UpdateCodeBadge();
        UpdateCodeEditGutter(actual);
        HighlightCodeEdit(actual);
    }

    private void OnCodeEditTextChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingCode) return;
        _codeEdit.Document.GetText(TextGetOptions.None, out var text);
        // O RichEditBox dispara TextChanged também quando só a FORMATAÇÃO muda; como a
        // recoloração é uma mudança de formatação, ignorar texto inalterado evita o laço.
        if (string.Equals(text, _lastCodeText, StringComparison.Ordinal)) return;
        _lastCodeText = text;

        // Por tecla, só o barato: numeração e agendar o autosave. NÃO tocamos em
        // _input/_md aqui (Sync() lê o editor ao salvar) e a recoloração é adiada.
        UpdateCodeEditGutter(text);
        Changed?.Invoke();
        ScheduleHighlight();
    }

    /// <summary>Agenda a recoloração para depois de uma breve pausa na digitação.</summary>
    private void ScheduleHighlight()
    {
        _highlightTimer ??= CreateHighlightTimer();
        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    private DispatcherTimer CreateHighlightTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_editing || !Model.IsCode) return;
            _codeEdit.Document.GetText(TextGetOptions.None, out var text);
            HighlightCodeEdit(text);
        };
        return timer;
    }

    /// <summary>
    /// Recolore apenas as linhas que mudaram desde a última recoloração (diff por
    /// prefixo/sufixo comum). Só faz o documento inteiro quando a mudança envolve um
    /// delimitador de comentário de bloco, que pode alterar a cor de outras linhas.
    /// </summary>
    private void HighlightCodeEdit(string text)
    {
        if (_highlightingCode) return;
        _highlightingCode = true;
        try
        {
            var doc = _codeEdit.Document;
            doc.BatchDisplayUpdates();
            var start = doc.Selection.StartPosition;
            var end = doc.Selection.EndPosition;

            int from, to;
            if (NeedsFullRecolor(_lastHighlightedText, text))
                (from, to) = (0, text.Length);
            else
                (from, to) = ChangedLineRange(_lastHighlightedText, text);

            CodeHighlighter.ApplyToDocument(doc, text, Model.Language, CodeDefaultColor, from, to);

            doc.Selection.SetRange(start, end);
            doc.ApplyDisplayUpdates();
            _lastHighlightedText = text;
        }
        finally { _highlightingCode = false; }
    }

    /// <summary>Faixa [início, fim) das linhas alteradas entre dois textos, no texto novo.</summary>
    private static (int from, int to) ChangedLineRange(string oldText, string newText)
    {
        var (a, _, bNew) = DiffRange(oldText, newText);
        var from = LineStartOf(newText, a);
        var to = LineEndOf(newText, Math.Max(a, bNew));
        return (from, to);
    }

    /// <summary>Prefixo comum (a) e fins do trecho divergente em cada texto.</summary>
    private static (int start, int oldEnd, int newEnd) DiffRange(string oldText, string newText)
    {
        int la = oldText.Length, lb = newText.Length;
        var max = Math.Min(la, lb);
        var a = 0;
        while (a < max && oldText[a] == newText[a]) a++;
        var s = 0;
        while (s < max - a && oldText[la - 1 - s] == newText[lb - 1 - s]) s++;
        return (a, la - s, lb - s);
    }

    /// <summary>
    /// Verdadeiro quando a mudança pode afetar a cor de outras linhas: só ocorre com
    /// comentário de bloco (SQL: /* */, PowerShell: &lt;# #&gt;), nas demais a mudança é local.
    /// </summary>
    private bool NeedsFullRecolor(string oldText, string newText)
    {
        var open = Model.Language switch { "sql" => "/*", "powershell" => "<#", _ => null };
        if (open is null) return false;
        var close = Model.Language == "sql" ? "*/" : "#>";
        var (a, bOld, bNew) = DiffRange(oldText, newText);
        return SegmentHasDelimiter(oldText, a, bOld, open, close)
            || SegmentHasDelimiter(newText, a, bNew, open, close);
    }

    private static bool SegmentHasDelimiter(string text, int from, int to, string open, string close)
    {
        var lo = Math.Max(0, from - 1); // -1/+1: pega delimitador partido pela borda do diff
        var hi = Math.Min(text.Length, to + 1);
        if (hi <= lo) return false;
        var seg = text.Substring(lo, hi - lo);
        return seg.Contains(open, StringComparison.Ordinal) || seg.Contains(close, StringComparison.Ordinal);
    }

    /// <summary>Espelha o conteúdo do editor de código no _input (fonte de verdade).</summary>
    private void SyncCodeToInput(string codeText)
    {
        var normalized = ToEditorText(codeText); // '\r' → '\r\n' para o TextBox
        if (!string.Equals(ToEditorText(_input.Text), normalized, StringComparison.Ordinal))
            _input.Text = normalized; // dispara _input.TextChanged → Model.Content, _md, Changed
    }

    private async void OnCodeEditPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true; // substitui a colagem rica padrão por texto puro
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Text)) return;
        string text;
        try { text = await content.GetTextAsync(); }
        catch { return; }
        text = text.Replace("\r\n", "\r").Replace('\n', '\r');
        // SetText insere o trecho inteiro de uma vez (TypeText "digitava" caractere a
        // caractere e travava em códigos grandes); depois coloca o cursor após o texto.
        var sel = _codeEdit.Document.Selection;
        sel.SetText(TextSetOptions.None, text);
        sel.SetRange(sel.EndPosition, sel.EndPosition);
    }

    private void OnCodeEditKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_editing) return;

        if (e.Key == Windows.System.VirtualKey.Tab)
        {
            e.Handled = true; // não move o foco: recua/desrecua
            ReindentSelection(dedent: IsShiftDown());
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            // NÃO marcamos Handled: o RichEditBox insere a quebra sozinho e marcar Handled
            // não impede isso (resultava em linha dupla). Só reaplicamos o recuo da linha
            // anterior na nova linha, depois que a quebra padrão já aconteceu.
            var indent = CurrentLineIndent();
            if (indent.Length > 0)
                DispatcherQueue.TryEnqueue(() => _codeEdit.Document.Selection.TypeText(indent));
        }
    }

    /// <summary>Tab/Shift+Tab: recua ou desrecua a seleção (ou a linha do cursor).</summary>
    private void ReindentSelection(bool dedent)
    {
        var doc = _codeEdit.Document;
        doc.GetText(TextGetOptions.None, out var all);
        int s = doc.Selection.StartPosition, e = doc.Selection.EndPosition;
        if (s > e) (s, e) = (e, s);

        if (s == e) // cursor isolado
        {
            if (dedent) DedentLine(all, s);
            else doc.Selection.TypeText(new string(' ', IndentWidth));
            return;
        }

        // seleção: recua/desrecua todas as linhas tocadas, mantendo o bloco selecionado.
        int blockStart = LineStartOf(all, s);
        int blockEnd = LineEndOf(all, e);
        var block = all[blockStart..blockEnd];
        var sb = new StringBuilder(block.Length + 16);
        int i = 0;
        var lineStart = true;
        while (i < block.Length)
        {
            if (lineStart)
            {
                if (!dedent) sb.Append(' ', IndentWidth);
                else
                {
                    var r = 0;
                    while (r < IndentWidth && i < block.Length && block[i] == ' ') { i++; r++; }
                    if (r == 0 && i < block.Length && block[i] == '\t') i++;
                }
            }
            if (i >= block.Length) break;
            var c = block[i];
            sb.Append(c);
            lineStart = c is '\r' or '\n';
            i++;
        }
        var newBlock = sb.ToString();
        var newText = all[..blockStart] + newBlock + all[blockEnd..];
        SetCodeEditText(newText, blockStart, blockStart + newBlock.Length);
    }

    /// <summary>Remove um nível de recuo do início da linha que contém <paramref name="caret"/>.</summary>
    private void DedentLine(string all, int caret)
    {
        int ls = LineStartOf(all, caret);
        var r = 0;
        while (r < IndentWidth && ls + r < all.Length && all[ls + r] == ' ') r++;
        if (r == 0 && ls < all.Length && all[ls] == '\t') r = 1;
        if (r == 0) return;
        var newText = all[..ls] + all[(ls + r)..];
        var newCaret = caret - Math.Min(r, Math.Max(0, caret - ls));
        SetCodeEditText(newText, newCaret, newCaret);
    }

    /// <summary>Recuo (espaços/tabs) no início da linha atual, até o cursor.</summary>
    private string CurrentLineIndent()
    {
        var doc = _codeEdit.Document;
        doc.GetText(TextGetOptions.None, out var all);
        int caret = doc.Selection.StartPosition;
        int ls = LineStartOf(all, caret);
        int p = ls;
        while (p < all.Length && p < caret && (all[p] == ' ' || all[p] == '\t')) p++;
        return all[ls..p];
    }

    /// <summary>Substitui todo o texto do editor e reposiciona a seleção, recolorindo.</summary>
    private void SetCodeEditText(string text, int selStart, int selEnd)
    {
        var doc = _codeEdit.Document;
        _loadingCode = true;
        doc.SetText(TextSetOptions.None, text);
        doc.Selection.SetRange(Math.Clamp(selStart, 0, text.Length), Math.Clamp(selEnd, 0, text.Length));
        _loadingCode = false;
        doc.GetText(TextGetOptions.None, out var actual);
        _lastCodeText = actual;
        UpdateCodeEditGutter(actual);
        Changed?.Invoke();
        ScheduleHighlight();
    }

    private static int LineStartOf(string s, int pos)
    {
        int i = Math.Clamp(pos, 0, s.Length);
        while (i > 0 && s[i - 1] != '\r' && s[i - 1] != '\n') i--;
        return i;
    }

    private static int LineEndOf(string s, int pos)
    {
        int i = Math.Clamp(pos, 0, s.Length);
        while (i < s.Length && s[i] != '\r' && s[i] != '\n') i++;
        return i;
    }

    private static bool IsShiftDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

    private void UpdateCodeBadge()
    {
        var name = CodeLanguages.ById(Model.Language)?.Name ?? Model.Language ?? "";
        _codeLangBadge.Text = name;
        _codeEditBadge.Text = name;
    }

    /// <summary>Numera as linhas do conteúdo de leitura (a partir do _input).</summary>
    private void UpdateCodeGutter() => _codeGutter.Text = BuildGutterText(CountLines(_input.Text));

    /// <summary>Numera as linhas do editor (texto com '\r' do RichEditBox).</summary>
    private void UpdateCodeEditGutter(string codeText)
    {
        var lines = CountLines(codeText);
        if (lines == _lastGutterLines) return; // só reconstrói quando o nº muda
        _lastGutterLines = lines;
        _codeEditGutter.Text = BuildGutterText(lines);
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        var count = 1;
        foreach (var ch in text)
            if (ch == '\n' || ch == '\r') count++;
        // '\r\n' conta como uma quebra só (desconta o par).
        var pairs = 0;
        for (var i = 1; i < text.Length; i++)
            if (text[i] == '\n' && text[i - 1] == '\r') pairs++;
        return count - pairs;
    }

    private static string BuildGutterText(int lines)
    {
        var sb = new StringBuilder(lines * 3);
        for (var n = 1; n <= lines; n++)
        {
            if (n > 1) sb.Append('\n');
            sb.Append(n);
        }
        return sb.ToString();
    }

    // ── construtores de UI compartilhados entre leitura e edição ──────────────

    private static TextBlock MakeGutter(FontFamily font, Brush foreground) => new()
    {
        FontFamily = font,
        FontSize = 13,
        TextAlignment = TextAlignment.Right,
        Foreground = foreground,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false,
    };

    private static TextBlock MakeLangBadge(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = PillTextBrush,
        HorizontalAlignment = HorizontalAlignment.Right,
        Opacity = 0.5,
        IsHitTestVisible = false,
    };

    /// <summary>Calha numérica num scroll vertical próprio (sem barra), sincronizado por
    /// código ao scroll do conteúdo — fica "presa" na horizontal e acompanha a vertical.</summary>
    private static ScrollViewer MakeGutterScroller(TextBlock gutter) => new()
    {
        Content = gutter,
        MaxHeight = BlockMaxHeight,
        VerticalAlignment = VerticalAlignment.Top,
        VerticalScrollMode = ScrollMode.Enabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollMode = ScrollMode.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        IsHitTestVisible = false, // não rola sozinha; só acompanha o conteúdo
        Background = new SolidColorBrush(Colors.Transparent),
    };

    /// <summary>
    /// Monta a grade do bloco de código: badge (topo, à direita, fixa) sobre uma linha com
    /// a calha de numeração à esquerda e o conteúdo rolável (leitura ou edição) à direita.
    /// O conteúdo é seu próprio scroller (viewport de 300px, com barras visíveis); a calha
    /// é sincronizada por fora.
    /// </summary>
    private static Grid MakeCodeGrid(TextBlock badge, FrameworkElement gutterScroll, Brush gutterLine, FrameworkElement content)
    {
        var gutterBorder = new Border
        {
            Child = gutterScroll,
            BorderBrush = gutterLine,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(0, 0, 10, 0),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(gutterBorder, 0);
        Grid.SetColumn(content, 1);
        body.Children.Add(gutterBorder);
        body.Children.Add(content);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(badge, 0);
        Grid.SetRow(body, 1);
        grid.Children.Add(badge);
        grid.Children.Add(body);
        return grid;
    }

    // ── menus de contexto ────────────────────────────────────────────────────

    private static void CopyText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var pkg = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        pkg.SetText(ToEditorText(text));
        Clipboard.SetContent(pkg);
    }

    // Localiza o RichTextBlock interno do Markdown e substitui apenas o menu de
    // contexto (clique direito) pelo personalizado, preservando a seleção de texto.
    private void ConfigureMarkdownContextMenu()
    {
        if (_mdRichText is not null) return; // configurado uma única vez
        _mdRichText = FindDescendant<RichTextBlock>(_md);
        if (_mdRichText is null) return;

        var mdMenu = BuildInputMenu();
        mdMenu.Opening += (s, _) =>
        {
            var menu = (MenuFlyout)s!;
            menu.Items.Clear();
            PopulateInputMenu(menu, _mdRichText.SelectedText);
        };
        _mdRichText.ContextFlyout = mdMenu;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }

    private MenuFlyout BuildSeparatorMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(MakeMenuItem("Inserir input acima",  "", () => InsertAboveRequested?.Invoke(this)));
        menu.Items.Add(MakeMenuItem("Inserir input abaixo", "", () => InsertBelowRequested?.Invoke(this)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MakeMenuItem("Excluir separador", "", () => DeleteRequested?.Invoke(this)));
        return menu;
    }

    private MenuFlyout BuildInputMenu(string? selectedText = null)
    {
        var menu = new MenuFlyout();
        PopulateInputMenu(menu, selectedText);
        return menu;
    }

    private void PopulateInputMenu(MenuFlyout menu, string? selectedText)
    {
        if (!string.IsNullOrEmpty(selectedText))
        {
            var text = selectedText;
            menu.Items.Add(MakeMenuItem("Copiar", "", () => CopyText(text)));
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        menu.Items.Add(MakeMenuItem("Inserir input acima",  "", () => InsertAboveRequested?.Invoke(this)));
        menu.Items.Add(MakeMenuItem("Inserir input abaixo", "", () => InsertBelowRequested?.Invoke(this)));
        menu.Items.Add(MakeMenuItem("Adicionar separador abaixo", "", () => InsertSeparatorBelowRequested?.Invoke(this)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MakeMenuItem("Editar",       "", EnterInputEdit));
        menu.Items.Add(MakeMenuItem("Adicionar label", "", EnterLabelEdit));
        menu.Items.Add(BuildCodeSubMenu());
        menu.Items.Add(MakeMenuItem("Inserir imagem", "", async () => await PickImageAsync()));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MakeMenuItem("Excluir", "", () => DeleteRequested?.Invoke(this)));
    }

    private MenuFlyout BuildLabelMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(MakeMenuItem("Editar",        "", EnterLabelEdit));
        menu.Items.Add(MakeMenuItem("Excluir label", "", () =>
        {
            Model.Label = null;
            UpdateLabelDisplay();
            Changed?.Invoke();
        }));
        return menu;
    }

    private MenuFlyoutSubItem BuildCodeSubMenu()
    {
        var sub = new MenuFlyoutSubItem { Text = "Bloco de código", Icon = new FontIcon { Glyph = "" } };
        foreach (var lang in CodeLanguages.All)
        {
            var mi = new MenuFlyoutItem { Text = lang.Name };
            mi.Click += (_, _) => SetCode(lang);
            sub.Items.Add(mi);
        }
        sub.Items.Add(new MenuFlyoutSeparator());
        var plainText = new MenuFlyoutItem { Text = "Texto puro" };
        plainText.Click += (_, _) => SetPlainText();
        sub.Items.Add(plainText);
        var plain = new MenuFlyoutItem { Text = "Markdown" };
        plain.Click += (_, _) => SetPlain();
        sub.Items.Add(plain);
        return sub;
    }

    private static MenuFlyoutItem MakeMenuItem(string text, string glyph, Action action)
    {
        var mi = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        mi.Click += (_, _) => action();
        return mi;
    }
}

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Pickers;

namespace totem;

public sealed partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attr, ref uint attrValue, uint attrSize);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // Mínimo da janela acompanha o piso do input: largura mínima do input
    // (ItemControl.MinInputWidth) + padding da lista (16*2) + folga p/ a borda.
    private const double MinWindowChrome = 32 + 20;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    // Cache local auto-salvo, protegido por DPAPI (só este usuário do Windows lê).
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "aec.totem", "cache.bin");

    private readonly TabView _tabs;
    private Border _tabStrip = null!;
    private DispatcherTimer? _saveDebounce;
    private bool _loaded;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();

        Activated += OnFirstActivated;

        Title = "Totem";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBarArea);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        ApplyMinWindowSize();

        var versao = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        menuVersionItem.Text = $"versão {versao?.ToString(3) ?? "1.0.0"}";
        menuDotNetItem.Text = RuntimeInformation.FrameworkDescription;

        // Faixa atrás da régua de abas (só a altura da régua; o conteúdo abaixo
        // mantém o fundo padrão do programa). As abas não selecionadas são
        // transparentes e se fundem aqui; a selecionada fica com a cor do conteúdo.
        // A cor da faixa/hover se adapta ao tema (claro/escuro).
        _tabStrip = new Border
        {
            Height = 40,
            VerticalAlignment = VerticalAlignment.Top,
        };
        contentRoot.Children.Add(_tabStrip);

        _tabs = BuildTabView();
        contentRoot.Children.Add(_tabs);

        ApplyAllTheme();

        AppWindow.Closing += async (_, args) =>
        {
            if (_isClosing) return;
            args.Cancel = true;
            _isClosing = true;
            _saveDebounce?.Stop();
            if (_loaded) await SaveCacheAsync();
            Application.Current.Exit();
        };

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var doc = await LoadCacheAsync();
        if (doc is { Tabs.Count: > 0 })
            LoadDocument(doc);
        else
            AddTab(new TotemTab { Name = "Aba 1", Items = [new TotemItem { Label = "" }] });

        _loaded = true;
        _tabs.TabItemsChanged += (_, _) => ScheduleSave();  // adicionar/fechar/reordenar
        _tabs.SelectionChanged += (_, _) => ScheduleSave(); // trocar de aba ativa
    }

    private void ApplyMinWindowSize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;

        presenter.PreferredMinimumWidth = (int)((ItemControl.MinInputWidth + MinWindowChrome) * scale);
        presenter.PreferredMinimumHeight = (int)(320 * scale);
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(uint));
    }

    // ── Abas (paginação) ─────────────────────────────────────────────────────

    private TabView BuildTabView()
    {
        var tv = new TabView
        {
            IsAddTabButtonVisible = true,
            CanReorderTabs = true,
            TabWidthMode = TabViewWidthMode.SizeToContent,
            CloseButtonOverlayMode = TabViewCloseButtonOverlayMode.OnPointerOver,
            // Suprime os balões de atalho (ex.: "Ctrl+F4") que o TabView mostra.
            KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden,
        };
        tv.AddTabButtonClick += (_, _) =>
            AddTab(new TotemTab { Name = $"Aba {tv.TabItems.Count + 1}", Items = [new TotemItem { Label = "" }] });
        tv.TabCloseRequested += (_, e) =>
        {
            tv.TabItems.Remove(e.Tab);
            if (tv.TabItems.Count == 0)
                AddTab(new TotemTab { Name = "Aba 1", Items = [new TotemItem { Label = "" }] });
        };
        tv.SelectionChanged += (_, _) => UpdateTabPipes();
        return tv;
    }

    private static string GetTabName(TabViewItem tab) =>
        tab.Header is Grid g
            ? (g.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "Aba")
            : tab.Header?.ToString() ?? "Aba";

    private static void SetTabName(TabViewItem tab, string name)
    {
        var tb = tab.Header is Grid g ? g.Children.OfType<TextBlock>().FirstOrDefault() : null;
        if (tb != null) tb.Text = name;
    }

    private void AddTab(TotemTab model)
    {
        var page = new TotemPage(model);
        page.Changed += ScheduleSave;

        var pipe = new Border
        {
            Height = 2,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = TabTopBorderBrush,
            CornerRadius = new CornerRadius(1),
            Visibility = Visibility.Collapsed,
        };
        var header = new Grid();
        header.Children.Add(new TextBlock
        {
            Text = model.Name,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 2, 10, 0),
        });
        header.Children.Add(pipe);

        var item = new TabViewItem
        {
            Header = header,
            Content = page,
            IsClosable = false,
            KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        ApplySelectedTabStyle(item);
        item.ContextFlyout = BuildTabMenu(item);

        _tabs.TabItems.Add(item);
        _tabs.SelectedItem = item;
    }

    private void UpdateTabPipes()
    {
        foreach (var obj in _tabs.TabItems)
        {
            if (obj is not TabViewItem tab || tab.Header is not Grid g) continue;
            var pipe = g.Children.OfType<Border>().FirstOrDefault();
            if (pipe != null)
                pipe.Visibility = tab.IsSelected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // A faixa (strip) fica escura; abas não selecionadas são transparentes e se
    // fundem com ela; a aba selecionada usa a cor sólida de fundo do tema para
    // "destacar" da faixa escura e se aproximar visualmente da área de conteúdo.
    private static readonly SolidColorBrush TabTopBorderBrush =
        new(ItemControl.AccentColor);
    private Brush _tabStripBrush    = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 0, 0, 0));
    private Brush _tabSelectedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32));
    private Brush _tabRestBrush     = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    private Brush _tabHoverBrush    = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 255, 255, 255));

    private void ApplyAllTheme()
    {
        // ── botões da barra de título (min/max/fechar) ─────────────────────────
        var tb = AppWindow.TitleBar;
        var btnFg = Windows.UI.Color.FromArgb(255, 255, 255, 255);
        tb.ButtonForegroundColor         = btnFg;
        tb.ButtonHoverForegroundColor    = btnFg;
        tb.ButtonPressedForegroundColor  = btnFg;
        tb.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(100, btnFg.R, btnFg.G, btnFg.B);
        tb.ButtonHoverBackgroundColor    = Windows.UI.Color.FromArgb(32, 255, 255, 255);

        // ── faixa e cores das abas ─────────────────────────────────────────────
        _tabSelectedBrush = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        _tabStripBrush    = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 0, 0, 0));
        _tabRestBrush     = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        _tabHoverBrush    = new SolidColorBrush(Windows.UI.Color.FromArgb(25, 0xFF, 0xFF, 0xFF));

        if (_tabStrip is not null) _tabStrip.Background = _tabStripBrush;
        if (_tabs is not null)
            foreach (var obj in _tabs.TabItems)
                if (obj is TabViewItem it) ApplySelectedTabStyle(it);
    }

    private void ApplySelectedTabStyle(TabViewItem item)
    {
        item.Resources["TabViewItemHeaderBackground"] = _tabRestBrush;
        item.Resources["TabViewItemHeaderBackgroundPointerOver"] = _tabHoverBrush;
        item.Resources["TabViewItemHeaderBackgroundPressed"] = _tabHoverBrush;
        item.Resources["TabViewItemHeaderBackgroundSelected"] = _tabSelectedBrush;
        item.Resources["TabViewItemHeaderBackgroundSelectedPointerOver"] = _tabSelectedBrush;
        item.Resources["TabViewItemHeaderBackgroundSelectedPressed"] = _tabSelectedBrush;
    }

    private MenuFlyout BuildTabMenu(TabViewItem tab)
    {
        var menu = new MenuFlyout();

        var rename = new MenuFlyoutItem { Text = "Renomear", Icon = new FontIcon { Glyph = "" } };
        rename.Click += async (_, _) => await RenameTabAsync(tab);
        menu.Items.Add(rename);

        var delete = new MenuFlyoutItem { Text = "Excluir", Icon = new FontIcon { Glyph = "" } };
        delete.Click += (_, _) =>
        {
            _tabs.TabItems.Remove(tab);
            if (_tabs.TabItems.Count == 0)
                AddTab(new TotemTab { Name = "Aba 1" });
        };
        menu.Items.Add(delete);

        return menu;
    }

    private async Task RenameTabAsync(TabViewItem tab)
    {
        var input = new TextBox { Text = GetTabName(tab), SelectionStart = 0 };
        var dialog = new ContentDialog
        {
            Title = "Renomear aba",
            Content = input,
            PrimaryButtonText = "Salvar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            SetTabName(tab, input.Text.Trim());
            ScheduleSave();
        }
    }

    // ── Exportar ─────────────────────────────────────────────────────────────

    private async void ExportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var senha = await AskNewPasswordAsync();
        if (senha is null) return;

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "totem",
        };
        picker.FileTypeChoices.Add("Totem (criptografado)", new List<string> { ".ttm" });
        InitializePicker(picker);

        var arquivo = await picker.PickSaveFileAsync();
        if (arquivo is null) return;

        try
        {
            var json = JsonSerializer.Serialize(BuildDocument(), JsonOptions);
            var bytes = TotemCrypto.Encrypt(json, senha);
            await File.WriteAllBytesAsync(arquivo.Path, bytes);
            await InfoAsync("Exportado", $"Arquivo salvo com sucesso em:\n{arquivo.Path}");
        }
        catch (Exception ex)
        {
            await InfoAsync("Erro ao exportar", ex.Message);
        }
    }

    // ── Importar ─────────────────────────────────────────────────────────────

    private async void ImportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".ttm");
        InitializePicker(picker);

        var arquivo = await picker.PickSingleFileAsync();
        if (arquivo is null) return;

        var senha = await AskPasswordAsync();
        if (senha is null) return;

        TotemDocument? doc;
        try
        {
            var bytes = await File.ReadAllBytesAsync(arquivo.Path);
            var json = TotemCrypto.Decrypt(bytes, senha);
            doc = JsonSerializer.Deserialize<TotemDocument>(json);
        }
        catch (CryptographicException)
        {
            await InfoAsync("Falha na importação", "Senha incorreta ou arquivo inválido/corrompido.");
            return;
        }
        catch (Exception ex)
        {
            await InfoAsync("Falha na importação", ex.Message);
            return;
        }

        if (doc is null)
        {
            await InfoAsync("Falha na importação", "Não foi possível ler o conteúdo do arquivo.");
            return;
        }

        if (doc.Version > TotemDocument.CurrentVersion)
        {
            await InfoAsync("Versão incompatível",
                "Este arquivo foi criado por uma versão mais recente do Totem. Atualize o aplicativo para abri-lo.");
            return;
        }

        LoadDocument(doc);
        ScheduleSave();
    }

    // ── Cache automático (DPAPI / CurrentUser) ───────────────────────────────

    private void ScheduleSave()
    {
        if (!_loaded) return; // não persiste durante a carga inicial
        _saveDebounce ??= CreateDebounce();
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private DispatcherTimer CreateDebounce()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += async (_, _) => { timer.Stop(); await SaveCacheAsync(); };
        return timer;
    }

    private async Task SaveCacheAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(BuildDocument(), JsonOptions);
            var provider = new DataProtectionProvider("LOCAL=user");
            var input = CryptographicBuffer.CreateFromByteArray(Encoding.UTF8.GetBytes(json));
            var protectedBuffer = await provider.ProtectAsync(input);
            CryptographicBuffer.CopyToByteArray(protectedBuffer, out var protectedBytes);

            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            await File.WriteAllBytesAsync(CachePath, protectedBytes);
        }
        catch { /* cache é best-effort */ }
    }

    private async Task<TotemDocument?> LoadCacheAsync()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var protectedBytes = await File.ReadAllBytesAsync(CachePath);
            var provider = new DataProtectionProvider();
            var buffer = await provider.UnprotectAsync(CryptographicBuffer.CreateFromByteArray(protectedBytes));
            CryptographicBuffer.CopyToByteArray(buffer, out var data);
            var doc = JsonSerializer.Deserialize<TotemDocument>(Encoding.UTF8.GetString(data));
            // Ignora cache de uma versão mais nova (downgrade do app): recriar é mais
            // seguro que reinterpretar um formato que ainda não conhecemos.
            return doc is not null && doc.Version <= TotemDocument.CurrentVersion ? doc : null;
        }
        catch
        {
            return null;
        }
    }

    // ── Documento <-> UI ─────────────────────────────────────────────────────

    private TotemDocument BuildDocument()
    {
        var doc = new TotemDocument();
        foreach (var obj in _tabs.TabItems)
        {
            if (obj is TabViewItem tab && tab.Content is TotemPage page)
            {
                doc.Tabs.Add(new TotemTab
                {
                    Name = GetTabName(tab),
                    Items = page.GetItems(),
                });
            }
        }
        doc.SelectedTab = _tabs.SelectedIndex;
        return doc;
    }

    private void LoadDocument(TotemDocument doc)
    {
        _tabs.TabItems.Clear();
        foreach (var tab in doc.Tabs)
            AddTab(tab);
        if (_tabs.TabItems.Count == 0)
            AddTab(new TotemTab { Name = "Aba 1", Items = [new TotemItem { Label = "" }] });

        // Restaura a aba que estava ativa. Precisa ser adiado: o TabView redefine a
        // seleção quando os itens entram na árvore visual, sobrescrevendo um set
        // síncrono feito aqui. Aplicamos via DispatcherQueue, após a montagem.
        var alvo = doc.SelectedTab;
        if (alvo >= 0 && alvo < _tabs.TabItems.Count)
        {
            var item = _tabs.TabItems[alvo];
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_tabs.TabItems.Contains(item))
                    _tabs.SelectedItem = item;
            });
        }
    }

    // ── Diálogos ─────────────────────────────────────────────────────────────

    private async Task<string?> AskNewPasswordAsync()
    {
        var pwd = new PasswordBox { PlaceholderText = "Senha", Margin = new Thickness(0, 0, 0, 8) };
        var confirm = new PasswordBox { PlaceholderText = "Confirmar senha" };
        var error = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            FontSize = 12,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var panel = new StackPanel { Width = 300 };
        panel.Children.Add(new TextBlock
        {
            Text = "Defina uma senha. Ela criptografa o arquivo e será exigida na importação.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        panel.Children.Add(pwd);
        panel.Children.Add(confirm);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = "Exportar (.ttm)",
            Content = panel,
            PrimaryButtonText = "Exportar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrEmpty(pwd.Password))
            {
                error.Text = "Informe uma senha.";
                error.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
            else if (pwd.Password != confirm.Password)
            {
                error.Text = "As senhas não conferem.";
                error.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? pwd.Password : null;
    }

    private async Task<string?> AskPasswordAsync()
    {
        var pwd = new PasswordBox { PlaceholderText = "Senha", Width = 300 };
        var dialog = new ContentDialog
        {
            Title = "Importar (.ttm)",
            Content = pwd,
            PrimaryButtonText = "Importar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? pwd.Password : null;
    }

    private async Task InfoAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    // ── Pickers (unpackaged precisa do HWND) ─────────────────────────────────

    private void InitializePicker(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }
}

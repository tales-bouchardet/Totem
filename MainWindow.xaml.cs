using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace totem;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    // Window minimum follows the input floor: input's minimum width
    // (ItemControl.MinInputWidth) + a bit of slack for the window chrome.
    private const double MinWindowChrome = 32 + 20;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    // Local auto-saved cache, protected by DPAPI (only this Windows user can read it).
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "aec.totem", "cache.bin");

    private DispatcherTimer? _saveDebounce;
    private bool _loaded;

    private Point _tabDragStart;
    private TabItem? _tabDragSource;

    // Always the last entry in Tabs.Items: a "+" that looks/behaves like the old
    // standalone button but flows in the same WrapPanel row as the real tabs.
    private TabItem? _addTabItem;

    public MainWindow()
    {
        InitializeComponent();

        MinWidth = ItemControl.MinInputWidth + MinWindowChrome;
        MinHeight = 320;

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"versão {version?.ToString(3) ?? "1.0.0"}";
        DotNetText.Text = ".NET Framework 4.6.2";

        App.DialogHost = RootContentDialogHost;

        Closing += (_, _) =>
        {
            _saveDebounce?.Stop();
            if (_loaded) SaveCache();
        };

        var doc = LoadCache();
        if (doc is { Tabs.Count: > 0 })
            LoadDocument(doc);
        else
            AddTab(new TotemTab { Name = "Aba 1", Items = [new TotemItem { Label = "" }] });

        _addTabItem = CreateAddTabItem();
        Tabs.Items.Add(_addTabItem);

        _loaded = true;
    }

    private TabItem CreateAddTabItem()
    {
        var button = new Wpf.Ui.Controls.Button
        {
            Content = "+",
            FontSize = 16,
            Width = 32,
            Height = 32,
            Margin = new Thickness(4),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };
        button.Click += (_, _) =>
            AddTab(new TotemTab { Name = $"Aba {Tabs.Items.Count}", Items = [new TotemItem { Label = "" }] });

        return new TabItem { Header = button, Tag = "AddButton", Focusable = false };
    }

    // ── Tabs ─────────────────────────────────────────────────────────────────

    private void AddTab(TotemTab model)
    {
        var page = new TotemPage(model);
        page.Changed += ScheduleSave;

        var tab = new TabItem
        {
            Header = model.Name,
            Content = page,
            AllowDrop = true,
        };
        tab.ContextMenu = BuildTabMenu(tab);
        tab.PreviewMouseLeftButtonDown += TabItem_PreviewMouseLeftButtonDown;
        tab.PreviewMouseMove += TabItem_PreviewMouseMove;
        tab.Drop += TabItem_Drop;

        var insertIndex = _addTabItem is null ? Tabs.Items.Count : Tabs.Items.IndexOf(_addTabItem);
        Tabs.Items.Insert(insertIndex, tab);
        Tabs.SelectedItem = tab;
        ScheduleSave();
    }

    private static string GetTabName(TabItem tab) => tab.Header?.ToString() ?? "Aba";

    private ContextMenu BuildTabMenu(TabItem tab)
    {
        var menu = new ContextMenu();

        var rename = new MenuItem { Header = "Renomear" };
        rename.Click += async (_, _) => await RenameTabAsync(tab);
        menu.Items.Add(rename);

        var delete = new MenuItem { Header = "Excluir" };
        delete.Click += (_, _) =>
        {
            Tabs.Items.Remove(tab);
            if (Tabs.Items.Count == (_addTabItem is null ? 0 : 1))
                AddTab(new TotemTab { Name = "Aba 1" });
            ScheduleSave();
        };
        menu.Items.Add(delete);

        return menu;
    }

    private async Task RenameTabAsync(TabItem tab)
    {
        var input = new TextBox { Text = GetTabName(tab) };
        var dialog = new Wpf.Ui.Controls.ContentDialog(RootContentDialogHost)
        {
            Title = "Renomear aba",
            Content = input,
            PrimaryButtonText = "Salvar",
            CloseButtonText = "Cancelar",
        };
        if (await dialog.ShowAsync() == Wpf.Ui.Controls.ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            tab.Header = input.Text.Trim();
            ScheduleSave();
        }
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => ScheduleSave();

    // ── Tab drag reorder ─────────────────────────────────────────────────────

    private void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _tabDragStart = e.GetPosition(null);
        _tabDragSource = sender as TabItem;
    }

    private void TabItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_tabDragSource is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _tabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var source = _tabDragSource;
        _tabDragSource = null;
        DragDrop.DoDragDrop(source, source, DragDropEffects.Move);
    }

    private void TabItem_Drop(object sender, DragEventArgs e)
    {
        if (sender is not TabItem target ||
            e.Data.GetData(typeof(TabItem)) is not TabItem source ||
            ReferenceEquals(source, target))
            return;

        var newIndex = Tabs.Items.IndexOf(target);
        Tabs.Items.Remove(source);
        Tabs.Items.Insert(newIndex, source);
        Tabs.SelectedItem = source;
        ScheduleSave();
    }

    // ── About popup ──────────────────────────────────────────────────────────

    private void AboutButton_Click(object sender, RoutedEventArgs e) => AboutPopup.IsOpen = !AboutPopup.IsOpen;

    // ── Export ───────────────────────────────────────────────────────────────

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var password = await AskNewPasswordAsync();
        if (password is null) return;

        var dialog = new SaveFileDialog { Filter = "Totem (criptografado)|*.ttm", FileName = "totem" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = JsonSerializer.Serialize(BuildDocument(), JsonOptions);
            var bytes = TotemCrypto.Encrypt(json, password);
            File.WriteAllBytes(dialog.FileName, bytes);
            await InfoAsync("Exportado", $"Arquivo salvo com sucesso em:\n{dialog.FileName}");
        }
        catch (Exception ex)
        {
            await InfoAsync("Erro ao exportar", ex.Message);
        }
    }

    // ── Import ───────────────────────────────────────────────────────────────

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Totem (criptografado)|*.ttm" };
        if (picker.ShowDialog() != true) return;

        var password = await AskPasswordAsync();
        if (password is null) return;

        TotemDocument? doc;
        try
        {
            var bytes = File.ReadAllBytes(picker.FileName);
            var json = TotemCrypto.Decrypt(bytes, password);
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

    // ── Automatic cache (DPAPI / CurrentUser) ───────────────────────────────

    private void ScheduleSave()
    {
        if (!_loaded) return; // don't persist during the initial load
        _saveDebounce ??= CreateDebounce();
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private DispatcherTimer CreateDebounce()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) => { timer.Stop(); SaveCache(); };
        return timer;
    }

    private void SaveCache()
    {
        try
        {
            var json = JsonSerializer.Serialize(BuildDocument(), JsonOptions);
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);

            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllBytes(CachePath, protectedBytes);
        }
        catch (Exception ex)
        {
            // cache is best-effort: never surfaces to the user, but worth a trace
            // so a silently-broken autosave can be diagnosed after the fact.
            Log.Error("SaveCache", ex);
        }
    }

    private TotemDocument? LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var protectedBytes = File.ReadAllBytes(CachePath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var doc = JsonSerializer.Deserialize<TotemDocument>(Encoding.UTF8.GetString(bytes));
            // Ignore a cache from a newer version (app downgrade): recreating it is safer
            // than reinterpreting a format we don't understand yet.
            return doc is not null && doc.Version <= TotemDocument.CurrentVersion ? doc : null;
        }
        catch (Exception ex)
        {
            Log.Error("LoadCache", ex);
            return null;
        }
    }

    // ── Document <-> UI ─────────────────────────────────────────────────────

    private TotemDocument BuildDocument()
    {
        var doc = new TotemDocument();
        foreach (var obj in Tabs.Items)
        {
            if (obj is TabItem tab && tab.Content is TotemPage page)
            {
                doc.Tabs.Add(new TotemTab { Name = GetTabName(tab), Items = page.GetItems() });
            }
        }
        doc.SelectedTab = Tabs.SelectedIndex;
        return doc;
    }

    private void LoadDocument(TotemDocument doc)
    {
        for (var i = Tabs.Items.Count - 1; i >= 0; i--)
            if (!ReferenceEquals(Tabs.Items[i], _addTabItem))
                Tabs.Items.RemoveAt(i);

        foreach (var tab in doc.Tabs)
            AddTab(tab);

        var realCount = Tabs.Items.Count - (_addTabItem is null ? 0 : 1);
        if (realCount == 0)
            AddTab(new TotemTab { Name = "Aba 1", Items = [new TotemItem { Label = "" }] });

        if (doc.SelectedTab >= 0 && doc.SelectedTab < realCount)
            Tabs.SelectedIndex = doc.SelectedTab;
    }

    // ── Dialogs ─────────────────────────────────────────────────────────────

    private async Task<string?> AskNewPasswordAsync()
    {
        var pwd = new PasswordBox { Margin = new Thickness(0, 0, 0, 8) };
        var confirm = new PasswordBox();

        var panel = new StackPanel { Width = 300 };
        panel.Children.Add(new TextBlock
        {
            Text = "Defina uma senha. Ela criptografa o arquivo e será exigida na importação.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        panel.Children.Add(new TextBlock { Text = "Senha", FontSize = 12, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(pwd);
        panel.Children.Add(new TextBlock { Text = "Confirmar senha", FontSize = 12, Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(confirm);

        var dialog = new Wpf.Ui.Controls.ContentDialog(RootContentDialogHost)
        {
            Title = "Exportar (.ttm)",
            Content = panel,
            PrimaryButtonText = "Exportar",
            CloseButtonText = "Cancelar",
        };

        if (await dialog.ShowAsync() != Wpf.Ui.Controls.ContentDialogResult.Primary) return null;

        if (string.IsNullOrEmpty(pwd.Password))
        {
            await InfoAsync("Exportar (.ttm)", "Informe uma senha.");
            return null;
        }
        if (pwd.Password != confirm.Password)
        {
            await InfoAsync("Exportar (.ttm)", "As senhas não conferem.");
            return null;
        }
        return pwd.Password;
    }

    private async Task<string?> AskPasswordAsync()
    {
        var pwd = new PasswordBox { Width = 300 };
        var dialog = new Wpf.Ui.Controls.ContentDialog(RootContentDialogHost)
        {
            Title = "Importar (.ttm)",
            Content = pwd,
            PrimaryButtonText = "Importar",
            CloseButtonText = "Cancelar",
        };
        return await dialog.ShowAsync() == Wpf.Ui.Controls.ContentDialogResult.Primary ? pwd.Password : null;
    }

    private async Task InfoAsync(string title, string message)
    {
        var dialog = new Wpf.Ui.Controls.ContentDialog(RootContentDialogHost)
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
        };
        await dialog.ShowAsync();
    }
}

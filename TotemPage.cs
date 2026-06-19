using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace totem;

/// <summary>
/// Conteúdo de uma aba: uma lista rolável de <see cref="ItemControl"/>.
/// Clicar com o botão direito num espaço vazio abre o menu para inserir um novo input.
/// </summary>
public sealed class TotemPage : Grid
{
    private readonly StackPanel _list;
    private readonly TextBlock _placeholder;

    public event Action? Changed; // borbulha para a janela persistir (cache)

    public TotemPage(TotemTab model)
    {
        Background = new SolidColorBrush(Colors.Transparent); // hit-test no espaço vazio
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        _placeholder = new TextBlock
        {
            Text = "Clique aqui com o botão direito para adicionar um novo input",
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            IsHitTestVisible = false,
        };
        Children.Add(_placeholder);

        var scroll = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _list = new StackPanel
        {
            Spacing = 12,
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        scroll.Content = _list;
        Children.Add(scroll);

        void UpdateListPadding(double w)
        {
            var h = Math.Min(60.0, Math.Max(0.0, (w - ItemControl.MinInputWidth) / 2.0));
            _list.Padding = new Thickness(h, 16, h, 16);
        }
        scroll.SizeChanged += (_, e) => UpdateListPadding(e.NewSize.Width);
        Loaded += (_, _) => UpdateListPadding(scroll.ActualWidth);

        // ContextFlyout: o framework abre o menu "Novo input" no clique direito.
        // Colocado no ScrollViewer (cobre todo o viewport), no StackPanel e no Grid
        // da página. Nos itens, o ItemControl tem o próprio ContextFlyout, que
        // prevalece (elemento mais interno vence).
        ContextFlyout = BuildAddMenu();
        scroll.ContextFlyout = BuildAddMenu();
        _list.ContextFlyout = BuildAddMenu();

        foreach (var item in model.Items)
            AddItem(item, notify: false);

        UpdatePlaceholder();
    }

    public List<TotemItem> GetItems()
    {
        var items = new List<TotemItem>();
        foreach (var child in _list.Children)
        {
            if (child is ItemControl ic)
            {
                ic.Sync();
                items.Add(ic.Model);
            }
        }
        return items;
    }

    private void AddItem(TotemItem model, int index = -1, bool notify = true)
    {
        var ctrl = new ItemControl(model);
        ctrl.InsertAboveRequested += c => InsertRelative(c, 0);
        ctrl.InsertBelowRequested += c => InsertRelative(c, 1);
        ctrl.InsertSeparatorBelowRequested += c => InsertRelative(c, 1, separator: true);
        ctrl.DeleteRequested += c => { _list.Children.Remove(c); UpdatePlaceholder(); Changed?.Invoke(); };
        ctrl.Changed += () => Changed?.Invoke();

        if (index < 0 || index >= _list.Children.Count)
            _list.Children.Add(ctrl);
        else
            _list.Children.Insert(index, ctrl);

        UpdatePlaceholder();
        if (notify) Changed?.Invoke();
    }

    private void InsertRelative(ItemControl reference, int offset, bool separator = false)
    {
        var i = _list.Children.IndexOf(reference);
        if (i < 0) { AddItem(new TotemItem()); return; }
        AddItem(separator ? new TotemItem { IsSeparator = true } : new TotemItem(), i + offset);
    }

    private void UpdatePlaceholder()
    {
        _placeholder.Visibility = _list.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private MenuFlyout BuildAddMenu()
    {
        var menu = new MenuFlyout();
        var add = new MenuFlyoutItem
        {
            Text = "Novo input",
            Icon = new FontIcon { Glyph = "" }, // Add
        };
        add.Click += (_, _) => AddItem(new TotemItem());
        menu.Items.Add(add);

        return menu;
    }
}

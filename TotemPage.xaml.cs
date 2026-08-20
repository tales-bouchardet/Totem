using System.Windows;
using System.Windows.Controls;

namespace totem;

public partial class TotemPage : UserControl
{
    public event Action? Changed;

    public TotemPage(TotemTab model)
    {
        InitializeComponent();

        foreach (var item in model.Items)
            AddItem(item, notify: false);

        UpdatePlaceholder();
    }

    public List<TotemItem> GetItems()
    {
        var items = new List<TotemItem>();
        foreach (var child in ItemsPanel.Children)
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
        ctrl.DeleteRequested += c => { ItemsPanel.Children.Remove(c); UpdatePlaceholder(); Changed?.Invoke(); };
        ctrl.Changed += () => Changed?.Invoke();

        if (index < 0 || index >= ItemsPanel.Children.Count)
            ItemsPanel.Children.Add(ctrl);
        else
            ItemsPanel.Children.Insert(index, ctrl);

        UpdatePlaceholder();
        if (notify) Changed?.Invoke();
    }

    private void InsertRelative(ItemControl reference, int offset, bool separator = false)
    {
        var i = ItemsPanel.Children.IndexOf(reference);
        if (i < 0) { AddItem(new TotemItem()); return; }
        AddItem(separator ? new TotemItem { IsSeparator = true } : new TotemItem(), i + offset);
    }

    private void UpdatePlaceholder() =>
        Placeholder.Visibility = ItemsPanel.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void NewInput_Click(object sender, RoutedEventArgs e) => AddItem(new TotemItem());
}

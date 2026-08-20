using System.Windows;
using System.Windows.Controls;

namespace totem;

public sealed class TabWrapPanel : Panel
{
    private const int MinLastRowItems = 2;

    private readonly List<UIElement> _tabs = new();
    private readonly List<int> _rowCounts = new();
    private UIElement? _addButton;
    private double _rowHeight;

    private static bool IsAddButton(UIElement child) =>
        child is FrameworkElement { Tag: "AddButton" };

    private double AddButtonWidth => _addButton?.DesiredSize.Width ?? 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        _tabs.Clear();
        _rowCounts.Clear();
        _addButton = null;
        _rowHeight = 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _rowHeight = Math.Max(_rowHeight, child.DesiredSize.Height);

            if (IsAddButton(child)) _addButton = child;
            else _tabs.Add(child);
        }

        var widest = 0.0;
        var rowWidth = 0.0;
        var count = 0;

        foreach (var tab in _tabs)
        {
            var width = tab.DesiredSize.Width;

            if (count > 0 && rowWidth + width > availableSize.Width)
            {
                _rowCounts.Add(count);
                widest = Math.Max(widest, rowWidth);
                rowWidth = 0;
                count = 0;
            }

            rowWidth += width;
            count++;
        }

        if (count > 0)
        {
            _rowCounts.Add(count);
            widest = Math.Max(widest, rowWidth);
        }

        var lastRow = _rowCounts.Count - 1;
        if (lastRow >= 0 && _rowCounts[lastRow] > 1 && rowWidth + AddButtonWidth > availableSize.Width)
        {
            _rowCounts[lastRow]--;
            _rowCounts.Add(1);
        }

        for (var last = _rowCounts.Count - 1; last > 0;)
        {
            if (_rowCounts[last] >= MinLastRowItems || _rowCounts[last - 1] <= MinLastRowItems) break;
            _rowCounts[last]++;
            _rowCounts[last - 1]--;
        }

        var totalWidth = double.IsInfinity(availableSize.Width)
            ? widest + AddButtonWidth
            : availableSize.Width;

        return new Size(totalWidth, _rowHeight * Math.Max(_rowCounts.Count, 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var index = 0;
        var y = 0.0;

        for (var row = 0; row < _rowCounts.Count; row++)
        {
            var count = _rowCounts[row];
            var isLast = row == _rowCounts.Count - 1;

            var rowWidth = isLast ? Math.Max(0, finalSize.Width - AddButtonWidth) : finalSize.Width;

            var used = 0.0;
            for (var i = 0; i < count; i++)
                used += _tabs[index + i].DesiredSize.Width;

            var share = Math.Max(0, rowWidth - used) / count;
            var x = 0.0;

            for (var i = 0; i < count; i++)
            {
                var tab = _tabs[index + i];
                var width = i == count - 1
                    ? Math.Max(0, rowWidth - x)
                    : tab.DesiredSize.Width + share;

                tab.Arrange(new Rect(x, y, width, _rowHeight));
                x += width;
            }

            if (isLast) _addButton?.Arrange(new Rect(x, y, AddButtonWidth, _rowHeight));

            index += count;
            y += _rowHeight;
        }

        if (_rowCounts.Count == 0)
            _addButton?.Arrange(new Rect(0, 0, AddButtonWidth, _rowHeight));

        return finalSize;
    }
}

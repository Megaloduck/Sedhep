using System;
using Avalonia;
using Avalonia.Controls;

namespace Sedhep.Controls;

/// <summary>
/// A shelf of equal cards that fills the width it is given. It fits as many columns as the
/// minimum card width allows and then shares every spare pixel between them, so a shelf never
/// ends in a ragged gap and the last row lines up with the rows above it.
/// </summary>
public class GalleryPanel : Panel
{
    /// <summary>Narrowest a card is allowed to get before another column is started.</summary>
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<GalleryPanel, double>(nameof(MinItemWidth), 170);

    static GalleryPanel()
    {
        AffectsMeasure<GalleryPanel>(MinItemWidthProperty);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    private int ColumnsFor(double width)
    {
        var minimum = MinItemWidth > 0 ? MinItemWidth : 1;
        return Math.Max(1, (int)Math.Floor(width / minimum));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return default;
        }

        // Inside a horizontal ScrollViewer the width is unbounded: fall back to one column.
        var width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? MinItemWidth
            : availableSize.Width;

        var columns = ColumnsFor(width);
        var cellWidth = width / columns;

        double total = 0;
        double rowHeight = 0;

        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(cellWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);

            if ((i + 1) % columns == 0)
            {
                total += rowHeight;
                rowHeight = 0;
            }
        }

        return new Size(width, total + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        var columns = ColumnsFor(finalSize.Width);
        var cellWidth = finalSize.Width / columns;

        var y = 0.0;

        for (var start = 0; start < Children.Count; start += columns)
        {
            // A row is as tall as its tallest card, so cards on the same row share a baseline.
            var rowHeight = 0.0;
            var end = Math.Min(start + columns, Children.Count);
            for (var i = start; i < end; i++)
            {
                rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);
            }

            for (var i = start; i < end; i++)
            {
                var column = i - start;
                Children[i].Arrange(new Rect(column * cellWidth, y, cellWidth, rowHeight));
            }

            y += rowHeight;
        }

        return finalSize;
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Sedhep.Controls;

/// <summary>
/// A Material Icons two-tone glyph. Each icon is an outline plus a lighter fill drawn beneath it;
/// both are painted with the inherited foreground, so the icon follows the theme and the state of
/// the control that holds it.
/// </summary>
public class TwoToneIcon : Control
{
    /// <summary>The outline, on a 24x24 grid.</summary>
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<TwoToneIcon, Geometry?>(nameof(Data));

    /// <summary>The lighter fill that sits under the outline. Optional: some icons have none.</summary>
    public static readonly StyledProperty<Geometry?> SoftDataProperty =
        AvaloniaProperty.Register<TwoToneIcon, Geometry?>(nameof(SoftData));

    public static readonly StyledProperty<double> SoftOpacityProperty =
        AvaloniaProperty.Register<TwoToneIcon, double>(nameof(SoftOpacity), 0.34);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<TwoToneIcon>();

    static TwoToneIcon()
    {
        AffectsRender<TwoToneIcon>(DataProperty, SoftDataProperty, SoftOpacityProperty, ForegroundProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Geometry? SoftData
    {
        get => GetValue(SoftDataProperty);
        set => SetValue(SoftDataProperty, value);
    }

    public double SoftOpacity
    {
        get => GetValue(SoftOpacityProperty);
        set => SetValue(SoftOpacityProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // The icon font's own grid: a little larger than the glyph so nothing is clipped.
        var width = double.IsNaN(Width) ? 24 : Width;
        var height = double.IsNaN(Height) ? 24 : Height;
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is null || Foreground is null)
        {
            return;
        }

        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        using (context.PushTransform(Matrix.CreateScale(size / 24, size / 24)))
        {
            if (SoftData is not null && SoftOpacity > 0)
            {
                using (context.PushOpacity(SoftOpacity))
                {
                    context.DrawGeometry(Foreground, null, SoftData);
                }
            }

            context.DrawGeometry(Foreground, null, Data);
        }
    }
}

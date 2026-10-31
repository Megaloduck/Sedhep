using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Sedhep.Infrastructure;

namespace Sedhep.Controls;

/// <summary>
/// Draws a QR code from a piece of text. Every module is a whole number of pixels, because a code
/// that has been resampled is a code a camera has to guess at. The quiet border comes from the
/// encoder, so the control never crops it away.
/// </summary>
public class QrCodeView : Control
{
    /// <summary>The text the code stands for; an invitation, in this app.</summary>
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<QrCodeView, string?>(nameof(Value));

    /// <summary>Colour of the dark modules. The paper underneath stays light.</summary>
    public static readonly StyledProperty<IBrush?> InkProperty =
        AvaloniaProperty.Register<QrCodeView, IBrush?>(nameof(Ink), Brushes.Black);

    private bool[,]? _modules;
    private string? _encodedFrom;

    static QrCodeView()
    {
        AffectsRender<QrCodeView>(ValueProperty, InkProperty);
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Ink
    {
        get => GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var modules = Modules;
        var ink = Ink;

        if (modules is null || ink is null)
        {
            return;
        }

        var grid = modules.GetLength(0);
        var available = Math.Min(Bounds.Width, Bounds.Height);

        if (grid <= 0 || available <= 0)
        {
            return;
        }

        // Whole pixels per module while they fit; below that, the code is simply too small to read.
        var module = Math.Max(1.0, Math.Floor(available / grid));
        var size = module * grid;
        var left = (Bounds.Width - size) / 2;
        var top = (Bounds.Height - size) / 2;

        var geometry = new StreamGeometry();

        using (var writer = geometry.Open())
        {
            for (var y = 0; y < grid; y++)
            {
                var x = 0;

                while (x < grid)
                {
                    if (!modules[x, y])
                    {
                        x++;
                        continue;
                    }

                    var start = x;

                    while (x < grid && modules[x, y])
                    {
                        x++;
                    }

                    // One rectangle per run of dark modules keeps the geometry small.
                    var rect = new Rect(left + start * module, top + y * module, (x - start) * module, module);
                    writer.BeginFigure(rect.TopLeft, isFilled: true);
                    writer.LineTo(rect.TopRight);
                    writer.LineTo(rect.BottomRight);
                    writer.LineTo(rect.BottomLeft);
                    writer.EndFigure(true);
                }
            }
        }

        context.DrawGeometry(ink, null, geometry);
    }

    private bool[,]? Modules
    {
        get
        {
            var value = Value;

            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (_encodedFrom == value)
            {
                return _modules;
            }

            _encodedFrom = value;
            _modules = QrCodes.Encode(value);
            return _modules;
        }
    }
}

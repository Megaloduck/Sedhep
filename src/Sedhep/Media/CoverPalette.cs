using System;
using Avalonia;
using Avalonia.Media;
using Sedhep.Domain;

namespace Sedhep.Media;

/// <summary>The wash a cover or a card is printed on, plus an ink that stays readable on it.</summary>
public sealed record CoverTone(IBrush Background, IBrush Ink);

/// <summary>
/// Artwork substitutes. Not every recipe carries a photograph, and an ingredient card never has
/// one, so both use the same flat, warm illustrations-by-colour idea: a two-stop wash picked from
/// the dish's name so the same recipe always comes back in the same colours.
/// </summary>
public static class CoverPalette
{
    private static readonly (string From, string To)[] Washes =
    {
        ("#F6B27A", "#E76F51"),
        ("#57B7A2", "#2A9D8F"),
        ("#F0CE86", "#E9A75E"),
        ("#6FA9B8", "#2F7E86"),
        ("#D98E6F", "#B8563F"),
        ("#A7BE84", "#6E9155")
    };

    private static readonly IBrush WashInk = Brush.Parse("#20180F");

    private static readonly (string Background, string Ink)[] Families =
    {
        ("#CFE7C8", "#2F6B3E"), // Produce
        ("#F6D2BE", "#8A3B22"), // Protein
        ("#F6DCAC", "#7A4E12"), // Spice
        ("#D5E9D1", "#35623F"), // Herb
        ("#EFDFC2", "#6C4E22"), // Grain
        ("#E5E6DF", "#4B5450"), // Dairy
        ("#F2D2D9", "#8A3746"), // Sweet
        ("#D8E6E4", "#2E5A57")  // Pantry
    };

    /// <summary>The wash behind a recipe that has no photograph.</summary>
    public static CoverTone ForTitle(string? title)
    {
        var (from, to) = Washes[StableIndex(title, Washes.Length)];

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse(from), 0),
                new GradientStop(Color.Parse(to), 1)
            }
        };

        return new CoverTone(brush, WashInk);
    }

    /// <summary>The sticky-note colour an almanac card is printed on.</summary>
    public static CoverTone ForFamily(IngredientFamily family)
    {
        var index = Math.Clamp((int)family, 0, Families.Length - 1);
        var (background, ink) = Families[index];

        return new CoverTone(Brush.Parse(background), Brush.Parse(ink));
    }

    /// <summary>
    /// A hash that does not change between runs. String.GetHashCode is randomised per process,
    /// which would repaint every cover on each launch.
    /// </summary>
    private static int StableIndex(string? value, int count)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var hash = 17;
        foreach (var c in value)
        {
            hash = unchecked(hash * 31 + char.ToLowerInvariant(c));
        }

        return Math.Abs(hash % count);
    }
}

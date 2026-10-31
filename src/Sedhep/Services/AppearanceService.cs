using System;
using Avalonia;
using Avalonia.Styling;
using Sedhep.Infrastructure;

namespace Sedhep.Services;

/// <summary>
/// Applies the two preferences that change how the app looks: the colour scheme and the text size.
/// Both are stored next to the cookbook so they survive a restart.
/// </summary>
public sealed class AppearanceService
{
    /// <summary>The scale each step of the "text size" setting applies.</summary>
    public static readonly double[] ScaleSteps = { 1.0, 1.12, 1.26 };

    private readonly AppSettingsStore _settings;

    public AppearanceService(AppSettingsStore settings)
    {
        _settings = settings;
    }

    public string Theme => _settings.Current.Theme;

    public double TextScale => _settings.Current.TextScale;

    public void Apply()
    {
        ApplyTheme(_settings.Current.Theme);
        ApplyScale(_settings.Current.TextScale);
    }

    public void SetTheme(string theme)
    {
        _settings.Current.Theme = theme;
        _settings.Save(_settings.Current);
        ApplyTheme(theme);
    }

    public void SetTextScale(double scale)
    {
        _settings.Current.TextScale = scale;
        _settings.Save(_settings.Current);
        ApplyScale(scale);
    }

    private static void ApplyTheme(string theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = theme switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private static void ApplyScale(double scale)
    {
        if (Application.Current is null)
        {
            return;
        }

        // Keep in step with the type scale declared in Styles/Palette.axaml: the scale is applied
        // on startup, so a number that disagrees there wins and the design quietly drifts.
        var resources = Application.Current.Resources;
        resources["FontBrand"] = 40 * scale;
        resources["FontDisplay"] = 31 * scale;
        resources["FontCook"] = 28 * scale;
        resources["FontTitle"] = 24 * scale;
        resources["FontH2"] = 19 * scale;
        resources["FontH3"] = 17 * scale;
        resources["FontBody"] = 15 * scale;
        resources["FontMeta"] = 13 * scale;
        resources["FontLabel"] = 11 * scale;
        resources["FontTab"] = 11.5 * scale;
        resources["FontNav"] = 14.5 * scale;

        // Two lines of an ingredient card's name, so a one-line name and a two-line name make the
        // same card and their families and counts line up across a row.
        resources["CardNameHeight"] = 46 * scale;
    }
}

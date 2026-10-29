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

        var resources = Application.Current.Resources;
        resources["FontBrand"] = 40 * scale;
        resources["FontDisplay"] = 33 * scale;
        resources["FontCook"] = 30 * scale;
        resources["FontTitle"] = 25 * scale;
        resources["FontH2"] = 20 * scale;
        resources["FontH3"] = 16.5 * scale;
        resources["FontBody"] = 15 * scale;
        resources["FontMeta"] = 13 * scale;
        resources["FontLabel"] = 11.5 * scale;
        resources["FontTab"] = 11 * scale;
    }
}

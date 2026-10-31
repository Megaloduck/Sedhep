using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace Sedhep.Services;

/// <summary>
/// A short piece of text on the clipboard. Pairing codes are text: copying one and pasting it on
/// the other device is the fallback when a camera or a screenshot is not at hand.
/// </summary>
public interface IClipboardService
{
    Task<string?> ReadTextAsync();

    Task SetTextAsync(string text);
}

public sealed class ClipboardService : IClipboardService
{
    public async Task<string?> ReadTextAsync()
    {
        var clipboard = Resolve();
        return clipboard is null ? null : await clipboard.GetTextAsync();
    }

    public async Task SetTextAsync(string text)
    {
        var clipboard = Resolve();
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private static IClipboard? Resolve()
    {
        switch (Application.Current?.ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                return desktop.MainWindow?.Clipboard;
            case ISingleViewApplicationLifetime singleView:
                return TopLevel.GetTopLevel(singleView.MainView)?.Clipboard;
            default:
                return null;
        }
    }
}

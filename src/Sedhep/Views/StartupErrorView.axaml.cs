using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace Sedhep.Views;

/// <summary>
/// Shown instead of the cookbook when the local storage cannot be opened. It says what went wrong,
/// where the cookbook lives, and what to do about it — the app never dies with a stack trace.
/// </summary>
public partial class StartupErrorView : UserControl
{
    private readonly string _folder;

    /// <summary>Used by the XAML loader in the designer.</summary>
    public StartupErrorView()
        : this("The cookbook could not be opened.", string.Empty, null)
    {
    }

    public StartupErrorView(string message, string path, string? hint)
    {
        InitializeComponent();

        _folder = path;

        MessageText.Text = message;
        PathText.Text = path;
        HintText.Text = hint ??
            "Set SEDHEP_DATA_DIR (or run with --data-dir <folder>) to keep the cookbook somewhere " +
            "this app is allowed to write, then start Sedhep again.";

        OpenFolderButton.IsVisible = Directory.Exists(path);
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_folder))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = _folder, UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening Explorer is a convenience; failing to do so is not worth another message.
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}

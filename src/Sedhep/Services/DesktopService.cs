using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using Sedhep.Infrastructure;

namespace Sedhep.Services;

/// <summary>
/// The things a desktop can do that a phone cannot: open a folder in the file manager, copy to the
/// clipboard, and restart into a different cookbook folder. The rest of the UI talks to this
/// interface so the view models stay free of process and window calls.
/// </summary>
public interface IDesktopService
{
    /// <summary>True only for the classic desktop host; the mobile and browser hosts return false.</summary>
    bool IsDesktop { get; }

    void OpenFolder(string directory);

    Task CopyTextAsync(string text);

    /// <summary>
    /// Starts a second Sedhep and closes this one. False when that is not possible.
    /// <paramref name="keepLocationOverrides"/> is used by "Try again": the folder may have been
    /// given with <c>--data-dir</c> and the new run should start from the same place.
    /// </summary>
    Task<bool> RestartAsync(bool keepLocationOverrides = false);

    void Quit();
}

public sealed class DesktopService : IDesktopService
{
    private readonly ILogger<DesktopService> _logger;

    public DesktopService(ILogger<DesktopService> logger)
    {
        _logger = logger;
    }

    public bool IsDesktop => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

    public void OpenFolder(string directory)
    {
        var full = CookbookFolder.Normalize(directory);
        if (full.Length == 0 || !Directory.Exists(full))
        {
            return;
        }

        try
        {
            // Let the shell decide what "open this folder" means on the platform.
            Process.Start(new ProcessStartInfo { FileName = full, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open {Folder} in the file manager", full);
        }
    }

    public async Task CopyTextAsync(string text)
    {
        var clipboard = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.Clipboard;

        if (clipboard is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not copy the cookbook path to the clipboard");
        }
    }

    public Task<bool> RestartAsync(bool keepLocationOverrides = false)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return Task.FromResult(false);
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return Task.FromResult(false);
        }

        var start = new ProcessStartInfo
        {
            FileName = processPath,
            WorkingDirectory = AppContext.BaseDirectory,
            // Start the executable directly. ShellExecute would hand the .exe to Windows'
            // attachment manager, which can show an "Open File - Security Warning" for a build
            // that came from another machine and stall the restart behind it.
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // When the app was started as "dotnet Sedhep.Desktop.dll", the muxer needs the assembly.
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entry = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entry))
            {
                return Task.FromResult(false);
            }

            start.Arguments = $"\"{entry}\"";
        }

        // The new process must not inherit a --data-dir from this run when the user chose a folder
        // in the app: the remembered folder should win. "Try again" keeps the override instead.
        if (!keepLocationOverrides)
        {
            Environment.SetEnvironmentVariable(FileSystemAppSandbox.DataDirectoryVariable, null);
            Environment.SetEnvironmentVariable(FileSystemAppSandbox.OneShotVariable, null);
        }

        try
        {
            Process.Start(start);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sedhep could not restart itself");
            return Task.FromResult(false);
        }

        desktop.Shutdown();
        return Task.FromResult(true);
    }

    public void Quit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}

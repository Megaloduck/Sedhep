using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using Sedhep.ViewModels;
using Sedhep.Views;

namespace Sedhep.Services;

/// <summary>
/// Opens the cookbook-location dialog and, when the user confirms a new folder, restarts Sedhep
/// after the dialog has closed. The restart has to happen outside the modal window: shutting down
/// while a dialog is on screen leaves the lifetime in an awkward state.
/// </summary>
public interface IDialogService
{
    bool CanShowCookbookLocation { get; }

    Task ShowCookbookLocationAsync();
}

public sealed class DialogService : IDialogService
{
    private readonly CookbookLocationService _locations;
    private readonly IDesktopService _desktop;
    private readonly ILogger<DialogService> _logger;
    private readonly ILogger<CookbookLocationViewModel> _locationLogger;

    public DialogService(
        CookbookLocationService locations,
        IDesktopService desktop,
        ILogger<DialogService> logger,
        ILogger<CookbookLocationViewModel> locationLogger)
    {
        _locations = locations;
        _desktop = desktop;
        _logger = logger;
        _locationLogger = locationLogger;
    }

    public bool CanShowCookbookLocation => _desktop.IsDesktop;

    public async Task ShowCookbookLocationAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return;
        }

        var viewModel = new CookbookLocationViewModel(_locations, _locationLogger);
        var window = new CookbookLocationWindow { DataContext = viewModel };

        await window.ShowDialog(owner);

        if (!viewModel.Saved)
        {
            return;
        }

        if (!await _desktop.RestartAsync())
        {
            _logger.LogError("The cookbook folder was saved but Sedhep could not restart");

            var notice = new NoticeWindow(
                "Saved, but not restarted",
                "Sedhep remembered the new cookbook folder, but it could not start a new copy of " +
                "itself. Close Sedhep and open it again to use the folder.");

            await notice.ShowDialog(owner);
        }
    }
}

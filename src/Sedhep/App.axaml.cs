using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;
using Sedhep.Infrastructure;
using Sedhep.Media;
using Sedhep.Navigation;
using Sedhep.Services;
using Sedhep.ViewModels;
using Sedhep.Views;

namespace Sedhep;

public partial class App : Application
{
    private IServiceProvider? _services;

    public static IServiceProvider Services => ((App)Current!)._services
        ?? throw new InvalidOperationException("Application services have not been initialized.");

    public override void Initialize()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            StartApplication();
        }
        catch (Exception ex)
        {
            // A cookbook that cannot be opened is a message, not a crash.
            ShowStartupError(ex);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartApplication()
    {
        var logger = Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Sedhep starting");

        var sandbox = Services.GetRequiredService<FileSystemAppSandbox>();
        if (sandbox.Problem is { } problem)
        {
            throw new CookbookStorageException(
                sandbox.RootDirectory,
                $"Sedhep could not use its cookbook folder at {sandbox.RootDirectory}. {problem}");
        }

        // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
        // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
        DisableAvaloniaDataAnnotationValidation();

        // Theme and text size are the user's, not the system's, once they have chosen them.
        Services.GetRequiredService<AppearanceService>().Apply();
        logger.LogInformation("Appearance applied");

        var shell = Services.GetRequiredService<ShellViewModel>();
        logger.LogInformation("Shell created");
        shell.ShowCookbook();
        logger.LogInformation("Cookbook shown");

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow { DataContext = shell };
                desktop.ShutdownRequested += (_, _) => ShutDownServices();
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                singleViewPlatform.MainView = new ShellView { DataContext = shell };
                break;
        }

        logger.LogInformation("Main window ready");

        // Nearby sharing listens from the moment the app opens so a recipe can arrive at any time.
        // It runs off the startup path: a slow or unusual network must never hold up the cookbook.
        var nearby = Services.GetRequiredService<INearbyShareService>();
        _ = Task.Run(async () =>
        {
            try
            {
                await nearby.StartAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Nearby sharing could not start");
            }
        });

        logger.LogInformation("Startup complete");
    }

    private void ShowStartupError(Exception exception)
    {
        string path;
        string message;
        string? hint = null;

        if (exception is CookbookStorageException storage)
        {
            path = storage.Path;
            message = storage.Message;
        }
        else
        {
            path = _services?.GetService<FileSystemAppSandbox>()?.RootDirectory ?? string.Empty;
            message = exception.Message;
            hint = "This is unexpected. The details are in the log next to the app's output.";
        }

        try
        {
            _services?.GetService<ILogger<App>>()?.LogError(exception, "Sedhep could not start");
        }
        catch
        {
            // Logging must not hide the original failure.
        }

        var view = new StartupErrorView(message, path, hint);

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                var window = new Window
                {
                    Title = "Sedhep",
                    Width = 432,
                    Height = 620,
                    MinWidth = 380,
                    MinHeight = 460,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = view
                };

                if (Current?.TryFindResource("Page", out var page) == true && page is Avalonia.Media.IBrush brush)
                {
                    window.Background = brush;
                }

                desktop.MainWindow = window;
                break;

            case ISingleViewApplicationLifetime singleViewPlatform:
                singleViewPlatform.MainView = view;
                break;
        }
    }

    private void ShutDownServices()
    {
        if (_services?.GetService<INearbyShareService>() is { } nearby)
        {
            _ = nearby.StopAsync();
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var sandbox = new FileSystemAppSandbox();

        services.AddLogging(builder =>
        {
#if DEBUG
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
#else
            builder.SetMinimumLevel(LogLevel.Warning);
#endif
        });

        services.AddSingleton(sandbox);
        services.AddSingleton<IClock, UtcClock>();

        services.AddSingleton(sp => new AppSettingsStore(
            sandbox.SettingsPath,
            sp.GetRequiredService<ILogger<AppSettingsStore>>()));

        services.AddSingleton<AppearanceService>();

        services.AddSingleton<IRecipePhotoStorage>(sp =>
            new FileSystemRecipePhotoStorage(
                sandbox.PhotosDirectory,
                sp.GetRequiredService<ILogger<FileSystemRecipePhotoStorage>>()));
        services.AddSingleton<IRecipePhotoLoader, RecipePhotoLoader>();

        // One open connection to the cookbook database, shared by every repository, created and
        // migrated on first use.
        services.AddSingleton(sp =>
        {
            var dbFixture = new SqliteDatabaseFixture(
                sandbox.DatabasePath,
                sp.GetRequiredService<ILogger<SqliteDatabaseFixture>>());
            return dbFixture.OpenConnection();
        });

        services.AddSingleton<IRecipeRepository>(sp => new SqliteRecipeRepository(
            sp.GetRequiredService<SqliteConnection>(),
            sp.GetRequiredService<ILogger<SqliteRecipeRepository>>(),
            sp.GetRequiredService<AppSettingsStore>().Current.DeviceId));

        services.AddSingleton<ICategoryRepository>(sp => new SqliteCategoryRepository(
            sp.GetRequiredService<SqliteConnection>(),
            sp.GetRequiredService<ILogger<SqliteCategoryRepository>>()));

        services.AddSingleton<ICookbookFileService>(sp => new CookbookFileService(
            sp.GetRequiredService<IRecipeRepository>(),
            sp.GetRequiredService<ICategoryRepository>(),
            sp.GetRequiredService<IRecipePhotoStorage>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<CookbookFileService>>()));

        services.AddSingleton<INearbyShareService>(sp => new LanPeerShareService(
            sp.GetRequiredService<AppSettingsStore>(),
            sp.GetRequiredService<ICookbookFileService>(),
            sp.GetRequiredService<FileSystemAppSandbox>(),
            sp.GetRequiredService<ILogger<LanPeerShareService>>()));

        services.AddSingleton<IFileDialogService, FileDialogService>();

        // The shell owns navigation; page view models are built lazily through factories so that
        // this graph never has a construction cycle.
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ICookbookNavigator>(sp => sp.GetRequiredService<ShellViewModel>());

        services.AddSingleton<Func<MainViewModel>>(sp => () => new MainViewModel(
            sp.GetRequiredService<IRecipeRepository>(),
            sp.GetRequiredService<ICategoryRepository>(),
            sp.GetRequiredService<IRecipePhotoLoader>(),
            sp.GetRequiredService<ICookbookNavigator>(),
            sp.GetRequiredService<ILogger<MainViewModel>>()));

        services.AddSingleton<Func<Recipe, RecipeViewModel>>(sp => recipe => new RecipeViewModel(
            sp.GetRequiredService<IRecipeRepository>(),
            sp.GetRequiredService<ICookbookNavigator>(),
            sp.GetRequiredService<IRecipePhotoLoader>(),
            sp.GetRequiredService<ILogger<RecipeViewModel>>(),
            recipe));

        services.AddSingleton<Func<Recipe?, RecipeEditorViewModel>>(sp => existing => new RecipeEditorViewModel(
            sp.GetRequiredService<IRecipeRepository>(),
            sp.GetRequiredService<ICategoryRepository>(),
            sp.GetRequiredService<IRecipePhotoStorage>(),
            sp.GetRequiredService<IRecipePhotoLoader>(),
            sp.GetRequiredService<ICookbookNavigator>(),
            sp.GetRequiredService<ILogger<RecipeEditorViewModel>>(),
            existing));

        services.AddSingleton<Func<Recipe, CookingModeViewModel>>(sp => recipe => new CookingModeViewModel(
            sp.GetRequiredService<ICookbookNavigator>(),
            recipe));

        services.AddSingleton<Func<CategoryManagerViewModel>>(sp => () => new CategoryManagerViewModel(
            sp.GetRequiredService<ICategoryRepository>(),
            sp.GetRequiredService<ICookbookNavigator>(),
            sp.GetRequiredService<ILogger<CategoryManagerViewModel>>()));

        services.AddSingleton<Func<SettingsViewModel>>(sp => () => new SettingsViewModel(
            sp.GetRequiredService<AppSettingsStore>(),
            sp.GetRequiredService<AppearanceService>(),
            sp.GetRequiredService<IRecipeRepository>(),
            sp.GetRequiredService<ICategoryRepository>(),
            sp.GetRequiredService<FileSystemAppSandbox>(),
            sp.GetRequiredService<ICookbookNavigator>(),
            sp.GetRequiredService<ILogger<SettingsViewModel>>()));

        services.AddSingleton<Func<Guid?, ShareViewModel>>(sp => recipeId => new ShareViewModel(
            sp.GetRequiredService<ICookbookFileService>(),
            sp.GetRequiredService<INearbyShareService>(),
            sp.GetRequiredService<IRecipeRepository>(),
            sp.GetRequiredService<IFileDialogService>(),
            sp.GetRequiredService<ICookbookNavigator>(),
            sp.GetRequiredService<ILogger<ShareViewModel>>(),
            recipeId));
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}

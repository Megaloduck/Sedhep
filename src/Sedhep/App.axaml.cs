using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
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

        // The recipes Sedhep ships with are added before the first page is built, so a new
        // cookbook opens onto real food instead of an empty grid. It happens once, and a failure
        // here must not keep the cook out of their cookbook.
        try
        {
            Services.GetRequiredService<SampleCookbookSeeder>().TrySeedAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sample recipes could not be added");
        }

        var shell = Services.GetRequiredService<ShellViewModel>();
        logger.LogInformation("Shell created");
        shell.ShowCookbook();
        logger.LogInformation("Cookbook shown");

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = CreateMainWindow(shell);
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

    /// <summary>
    /// The cookbook window, opened at the size it was left in. A narrow window is a valid choice
    /// on a desk, so the minimum stays small: the shell turns back into tabs there.
    /// </summary>
    private static Window CreateMainWindow(ShellViewModel shell)
    {
        var settings = Services.GetRequiredService<AppSettingsStore>();
        var window = new MainWindow { DataContext = shell };

        if (settings.Current.WindowWidth is { } width && settings.Current.WindowHeight is { } height)
        {
            window.Width = Math.Max(window.MinWidth, width);
            window.Height = Math.Max(window.MinHeight, height);
        }

        KeepOnScreen(window);

        window.Closing += (_, _) =>
        {
            // A maximised window keeps the size it will return to, not the screen's.
            if (window.WindowState != WindowState.Normal)
            {
                return;
            }

            var current = settings.Current;
            current.WindowWidth = window.Bounds.Width;
            current.WindowHeight = window.Bounds.Height;
            settings.Save(current);
        };

        return window;
    }

    /// <summary>A window sized on a monitor that is no longer here must not open off-screen.</summary>
    private static void KeepOnScreen(Window window)
    {
        var screen = window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        window.Width = Math.Max(window.MinWidth, Math.Min(window.Width, screen.WorkingArea.Width / scaling));
        window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, screen.WorkingArea.Height / scaling));
    }

    private void ShowStartupError(Exception exception)
    {
        try
        {
            _services?.GetService<ILogger<App>>()?.LogError(exception, "Sedhep could not start");
        }
        catch
        {
            // Logging must not hide the original failure.
        }

        var viewModel = new StartupErrorViewModel(
            exception,
            _services?.GetService<FileSystemAppSandbox>(),
            _services?.GetService<IDialogService>(),
            _services?.GetService<IDesktopService>());

        var view = new StartupErrorView(viewModel);

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                var window = new Window
                {
                    Title = "Sedhep",
                    Width = 580,
                    Height = 680,
                    MinWidth = 460,
                    MinHeight = 540,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = view,
                    Icon = TryLoadWindowIcon()
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

    private static WindowIcon? TryLoadWindowIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://Sedhep/Assets/app-icon.ico")));
        }
        catch (Exception)
        {
            // The icon is a nicety; a missing resource must not stop the recovery screen.
            return null;
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
        services.AddLogging(builder =>
        {
#if DEBUG
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
#else
            builder.SetMinimumLevel(LogLevel.Warning);
#endif
        });

        // The location note is read before the cookbook, because it decides where the cookbook is.
        services.AddSingleton(_ => new CookbookLocationStore());
        services.AddSingleton(sp => new FileSystemAppSandbox(sp.GetRequiredService<CookbookLocationStore>()));

        services.AddSingleton<IClock, UtcClock>();

        services.AddSingleton(sp => new AppSettingsStore(
            sp.GetRequiredService<FileSystemAppSandbox>().SettingsPath,
            sp.GetRequiredService<ILogger<AppSettingsStore>>()));

        services.AddSingleton<AppearanceService>();

        services.AddSingleton<IRecipePhotoStorage>(sp =>
            new FileSystemRecipePhotoStorage(
                sp.GetRequiredService<FileSystemAppSandbox>().PhotosDirectory,
                sp.GetRequiredService<ILogger<FileSystemRecipePhotoStorage>>()));
        services.AddSingleton<IRecipePhotoLoader, RecipePhotoLoader>();

        // One open connection to the cookbook database, shared by every repository, created and
        // migrated on first use. Keeping it behind the holder means the recovery screen can be
        // shown without trying to open a database that may be unreachable.
        services.AddSingleton(sp => new CookbookDatabase(
            sp.GetRequiredService<FileSystemAppSandbox>().DatabasePath,
            sp.GetRequiredService<ILogger<SqliteDatabaseFixture>>(),
            sp.GetRequiredService<ILogger<CookbookDatabase>>()));

        services.AddSingleton(sp => sp.GetRequiredService<CookbookDatabase>().Connection);

        services.AddSingleton<IRecipeRepository>(sp => new SqliteRecipeRepository(
            sp.GetRequiredService<SqliteConnection>(),
            sp.GetRequiredService<ILogger<SqliteRecipeRepository>>(),
            sp.GetRequiredService<AppSettingsStore>().Current.DeviceId));

        services.AddSingleton<ICategoryRepository>(sp => new SqliteCategoryRepository(
            sp.GetRequiredService<SqliteConnection>(),
            sp.GetRequiredService<ILogger<SqliteCategoryRepository>>()));

        services.AddSingleton(sp => new SampleCookbookSeeder(
            sp.GetRequiredService<IRecipeRepository>(),
            sp.GetRequiredService<ICategoryRepository>(),
            sp.GetRequiredService<AppSettingsStore>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<SampleCookbookSeeder>>()));

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
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<QrImageReader>();

        // Desktop recovery and the folder chooser. The services are registered on every platform;
        // they report IsDesktop false where the shell cannot open folders or restart the app.
        services.AddSingleton<IDesktopService, DesktopService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<CookbookLocationService>();

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

        services.AddSingleton<Func<AlmanacViewModel>>(sp => () => new AlmanacViewModel(
            sp.GetRequiredService<IRecipeRepository>(),
            sp.GetRequiredService<ICookbookNavigator>(),
            sp.GetRequiredService<ILogger<AlmanacViewModel>>()));

        services.AddSingleton<Func<SettingsViewModel>>(sp => () => new SettingsViewModel(
            sp.GetRequiredService<AppSettingsStore>(),
            sp.GetRequiredService<AppearanceService>(),
            sp.GetRequiredService<IRecipeRepository>(),
            sp.GetRequiredService<ICategoryRepository>(),
            sp.GetRequiredService<FileSystemAppSandbox>(),
            sp.GetRequiredService<CookbookLocationService>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IDesktopService>(),
            sp.GetRequiredService<ICookbookNavigator>(),
            sp.GetRequiredService<ILogger<SettingsViewModel>>()));

        services.AddSingleton<Func<Guid?, ShareViewModel>>(sp => recipeId => new ShareViewModel(
            sp.GetRequiredService<ICookbookFileService>(),
            sp.GetRequiredService<INearbyShareService>(),
            sp.GetRequiredService<IRecipeRepository>(),
            sp.GetRequiredService<IFileDialogService>(),
            sp.GetRequiredService<IClipboardService>(),
            sp.GetRequiredService<QrImageReader>(),
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

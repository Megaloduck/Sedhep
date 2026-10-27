using System;
using System.Linq;
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
        // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
        // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
        DisableAvaloniaDataAnnotationValidation();

        var shell = Services.GetRequiredService<ShellViewModel>();
        shell.ShowCookbook();

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow { DataContext = shell };
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                singleViewPlatform.MainView = new ShellView { DataContext = shell };
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var sandbox = new FileSystemAppSandbox();

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });

        services.AddSingleton<IClock, UtcClock>();
        services.AddSingleton<IRecipePhotoStorage>(sp =>
            new FileSystemRecipePhotoStorage(
                sandbox.PhotosDirectory,
                sp.GetRequiredService<ILogger<FileSystemRecipePhotoStorage>>()));
        services.AddSingleton<ICookbookTransferService, CookbookTransferService>();
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
            sp.GetRequiredService<ILogger<SqliteRecipeRepository>>()));

        services.AddSingleton<ICategoryRepository>(sp => new SqliteCategoryRepository(
            sp.GetRequiredService<SqliteConnection>(),
            sp.GetRequiredService<ILogger<SqliteCategoryRepository>>()));

        // The shell owns navigation; page view models are built lazily through factories so that
        // this graph never has a construction cycle.
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ICookbookNavigator>(sp => sp.GetRequiredService<ShellViewModel>());
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<Func<MainViewModel>>(sp => sp.GetRequiredService<MainViewModel>);

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

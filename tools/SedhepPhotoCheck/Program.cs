using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Sedhep;
using Sedhep.Domain;
using Sedhep.Infrastructure;
using Sedhep.Media;
using Sedhep.ViewModels;
using Sedhep.Views;

var failures = 0;

void Check(string what, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}");
    if (!ok)
    {
        failures++;
    }
}

// A real 1x1 PNG, so the image path is exercised rather than faked.
var png = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

var root = Path.Combine(Path.GetTempPath(), "sedhep-photo-check", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));

var fixture = new SqliteDatabaseFixture(Path.Combine(root, "cookbook.db"), loggerFactory.CreateLogger<SqliteDatabaseFixture>());
var connection = fixture.OpenConnection();
var recipes = new SqliteRecipeRepository(connection, loggerFactory.CreateLogger<SqliteRecipeRepository>());
var categories = new SqliteCategoryRepository(connection, loggerFactory.CreateLogger<SqliteCategoryRepository>());
var photoStorage = new FileSystemRecipePhotoStorage(Path.Combine(root, "photos"), loggerFactory.CreateLogger<FileSystemRecipePhotoStorage>());
var photoLoader = new RecipePhotoLoader(photoStorage, loggerFactory.CreateLogger<RecipePhotoLoader>());
var transfer = new CookbookTransferService(recipes, photoStorage, loggerFactory.CreateLogger<CookbookTransferService>(), new UtcClock());

MainViewModel main = null!;
ShellViewModel shell = null!;
shell = new ShellViewModel(
    () => main,
    recipe => new RecipeViewModel(recipes, shell, photoLoader, loggerFactory.CreateLogger<RecipeViewModel>(), recipe),
    existing => new RecipeEditorViewModel(recipes, categories, photoStorage, photoLoader, shell, loggerFactory.CreateLogger<RecipeEditorViewModel>(), existing),
    recipe => new CookingModeViewModel(shell, recipe),
    () => new CategoryManagerViewModel(categories, shell, loggerFactory.CreateLogger<CategoryManagerViewModel>()));
main = new MainViewModel(recipes, categories, transfer, shell, loggerFactory.CreateLogger<MainViewModel>());

// ---------------------------------------------------------------- attaching photos
var editor = new RecipeEditorViewModel(recipes, categories, photoStorage, photoLoader, shell, loggerFactory.CreateLogger<RecipeEditorViewModel>(), null);
Check("a new recipe starts with no photos", editor.Photos.Count == 0 && !editor.HasPhotos);

editor.Title = "Soto Ayam";
await editor.AddPhotoAsync(new MemoryStream(png), "dish.png");
Check("the photo is attached to the recipe", editor.Photos.Count == 1 && editor.HasPhotos);
Check("the first photo becomes the cover", editor.Photos[0].IsCover);

var first = editor.Photos[0].Photo;
var storedPath = Path.Combine(photoStorage.DefaultPhotosDirectory, first.RecipeId.ToString(), first.StorageId + ".jpg");
Check("the photo file is written to the device", File.Exists(storedPath));
Check("the recorded size matches the file", first.SizeBytes == new FileInfo(storedPath).Length && first.SizeBytes > 0);
Check("the original file name is kept", first.FileName == "dish.png");

await editor.AddPhotoAsync(new MemoryStream(png), "plated.png");
Check("a second photo is attached", editor.Photos.Count == 2);
Check("only the first photo is the cover", editor.Photos[0].IsCover && !editor.Photos[1].IsCover);

// ---------------------------------------------------------------- persisting
await editor.SaveCommand.ExecuteAsync(null);
var saved = (await recipes.GetAllAsync()).Single();
Check("both photos are persisted with the recipe", saved.Photos.Count == 2);
Check("the cover flag survives a reload", saved.Photos.Count(p => p.IsCover) == 1);

// ---------------------------------------------------------------- removing
var reloadedEditor = new RecipeEditorViewModel(recipes, categories, photoStorage, photoLoader, shell, loggerFactory.CreateLogger<RecipeEditorViewModel>(), saved);
Check("the editor shows the stored photos", reloadedEditor.Photos.Count == 2 && reloadedEditor.HasPhotos);

var coverThumbnail = reloadedEditor.Photos.First(p => p.IsCover);
reloadedEditor.RemovePhotoCommand.Execute(coverThumbnail);
Check("removing a photo updates the editor", reloadedEditor.Photos.Count == 1);
Check("the next photo is promoted to cover", reloadedEditor.Photos[0].IsCover);

await reloadedEditor.SaveCommand.ExecuteAsync(null);
var afterRemoval = (await recipes.GetAllAsync()).Single();
Check("the removal is persisted", afterRemoval.Photos.Count == 1 && afterRemoval.Photos.First().IsCover);

// ---------------------------------------------------------------- decoding
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var thumbnail = new RecipePhotoViewModel(afterRemoval.Photos.First());
thumbnail.Image = await photoLoader.LoadAsync(thumbnail.Photo);
Check("the stored photo decodes into a bitmap", thumbnail.Image is not null && thumbnail.HasImage);
if (thumbnail.Image is not null)
{
    Check("the decoded bitmap has real pixels", thumbnail.Image.PixelSize.Width > 0 && thumbnail.Image.PixelSize.Height > 0);
    Console.WriteLine($"        decoded size: {thumbnail.Image.PixelSize}");
}

var missing = new RecipePhotoViewModel(new RecipePhoto
{
    Id = Guid.NewGuid(),
    RecipeId = Guid.NewGuid(),
    StorageId = "does-not-exist"
});
checkMissing();

async void checkMissing()
{
    missing.Image = await photoLoader.LoadAsync(missing.Photo);
    Check("a missing photo falls back to the placeholder", !missing.HasImage);
}

// ---------------------------------------------------------------- rendering
var sink = new CapturingSink();
Avalonia.Logging.Logger.Sink = sink;

shell.ShowRecipe(afterRemoval);
var window = new Window { Content = new ShellView { DataContext = shell }, Width = 400, Height = 760 };
window.Show();
Pump();

Check("the recipe page renders with a photo and no binding errors",
    sink.Messages.All(m => !m.Contains("Binding", StringComparison.OrdinalIgnoreCase)));
foreach (var message in sink.Messages.Where(m => m.Contains("Binding", StringComparison.OrdinalIgnoreCase)).Take(4))
{
    Console.WriteLine($"        {message}");
}

sink.Messages.Clear();
shell.ShowEditor(afterRemoval);
Pump();
Check("the editor renders its photo list with no binding errors",
    sink.Messages.All(m => !m.Contains("Binding", StringComparison.OrdinalIgnoreCase)));

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");

try
{
    Directory.Delete(root, recursive: true);
}
catch (IOException)
{
    // The SQLite connection stays open for the life of the process.
}

return failures == 0 ? 0 : 1;

static void Pump()
{
    for (var i = 0; i < 5; i++)
    {
        Dispatcher.UIThread.RunJobs();
    }
}

internal sealed class CapturingSink : Avalonia.Logging.ILogSink
{
    public List<string> Messages { get; } = new();

    public bool IsEnabled(Avalonia.Logging.LogEventLevel level, string area)
        => level >= Avalonia.Logging.LogEventLevel.Warning;

    public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate)
        => Messages.Add($"[{level}] {area}: {messageTemplate}");

    public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
        => Messages.Add($"[{level}] {area}: {messageTemplate}");
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sedhep.Domain;
using Sedhep.Infrastructure;
using Xunit;

namespace Sedhep.Tests;

public sealed class CookbookFileTests
{
    private static Recipe PhotoRecipe(string title, Guid categoryId) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Description = "The Sunday soup.",
        CategoryId = categoryId,
        Servings = 6,
        PrepMinutes = 25,
        CookMinutes = 40,
        Notes = "Skim the foam early.",
        Tags = { "sunday", "mom's" },
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task A_cookbook_survives_a_round_trip_through_a_file()
    {
        using var source = new TestCookbook("source");
        using var target = new TestCookbook("target");

        var soup = (await source.Categories.GetAllAsync()).First(c => c.Name == "Soup");
        var recipe = PhotoRecipe("Soto Ayam", soup.Id);
        recipe.Ingredients.Add(new Ingredient
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            SortOrder = 1,
            Quantity = "1",
            Unit = "kg",
            Name = "chicken"
        });
        recipe.Steps.Add(new RecipeStep
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Boil the chicken.",
            Minutes = 30
        });

        await source.Photos.StoreAsync(
            recipe.Id.ToString(),
            "photo-1",
            new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }));

        recipe.Photos.Add(new RecipePhoto
        {
            Id = Guid.NewGuid(),
            RecipeId = recipe.Id,
            StorageId = "photo-1",
            FileName = "soto.jpg",
            SizeBytes = 4,
            CapturedAt = DateTimeOffset.UtcNow,
            IsCover = true
        });

        await source.Recipes.SaveAsync(recipe);

        using var package = new MemoryStream();
        var summary = await source.Files.WriteAsync(package);
        Assert.Equal(1, summary.RecipeCount);
        Assert.Equal(1, summary.PhotoCount);

        package.Position = 0;
        var result = await target.Files.ReadAsync(package);

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Replaced);
        Assert.Equal(1, result.PhotosRestored);

        var copy = await target.Recipes.GetByIdAsync(recipe.Id);
        Assert.NotNull(copy);
        Assert.Equal("Soto Ayam", copy!.Title);
        Assert.Equal("Skim the foam early.", copy.Notes);
        Assert.Equal(new[] { "sunday", "mom's" }, copy.Tags);
        Assert.Equal("Soup", copy.CategoryName);
        Assert.Single(copy.Ingredients);
        Assert.Single(copy.Steps);
        Assert.Single(copy.Photos);
        Assert.True(await target.Photos.ExistsAsync(recipe.Id.ToString(), "photo-1"));
    }

    [Fact]
    public async Task Importing_keeps_a_newer_local_copy_and_says_so()
    {
        using var source = new TestCookbook("source");
        using var target = new TestCookbook("target");

        var id = Guid.NewGuid();
        var local = new Recipe
        {
            Id = id,
            Title = "Rendang (my version)",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Five saves on this device, so the local copy is at revision 5.
        for (var i = 0; i < 5; i++)
        {
            await target.Recipes.SaveAsync(local);
        }

        Assert.Equal(5, local.Revision);

        var fromFile = new Recipe
        {
            Id = id,
            Title = "Rendang (their version)",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await source.Recipes.SaveAsync(fromFile);

        using var package = new MemoryStream();
        await source.Files.WriteAsync(package, new[] { id });
        package.Position = 0;

        var result = await target.Files.ReadAsync(package);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Imported);
        Assert.Single(result.Conflicts);
        Assert.NotEmpty(result.Warnings);

        var after = await target.Recipes.GetByIdAsync(id);
        Assert.Equal("Rendang (my version)", after!.Title);
    }

    [Fact]
    public async Task A_newer_incoming_revision_still_requires_an_explicit_conflict_decision()
    {
        using var source = new TestCookbook("source");
        using var target = new TestCookbook("target");

        var id = Guid.NewGuid();
        var local = new Recipe
        {
            Id = id, Title = "Mine", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        await target.Recipes.SaveAsync(local);

        var incoming = new Recipe
        {
            Id = id, Title = "Theirs", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        for (var i = 0; i < 3; i++)
        {
            await source.Recipes.SaveAsync(incoming);
        }

        using var package = new MemoryStream();
        await source.Files.WriteAsync(package, new[] { id });
        package.Position = 0;

        var policy = new Policy(_ => ConflictDecision.KeepLocal);
        var result = await target.Files.ReadAsync(package, policy);

        Assert.Single(policy.Seen);
        Assert.Equal(3, policy.Seen[0].IncomingRevision);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Replaced);
        Assert.Equal("Mine", (await target.Recipes.GetByIdAsync(id))!.Title);
    }

    [Fact]
    public async Task A_tie_is_offered_to_the_user_and_the_incoming_copy_can_win()
    {
        using var source = new TestCookbook("source");
        using var target = new TestCookbook("target");

        var id = Guid.NewGuid();
        await target.Recipes.SaveAsync(new Recipe
        {
            Id = id, Title = "Mine", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await source.Recipes.SaveAsync(new Recipe
        {
            Id = id, Title = "Theirs", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });

        using var package = new MemoryStream();
        await source.Files.WriteAsync(package, new[] { id });
        package.Position = 0;

        var policy = new Policy(_ => ConflictDecision.UseIncoming);
        var result = await target.Files.ReadAsync(package, policy);

        Assert.Single(policy.Seen);
        Assert.Single(result.Conflicts);
        Assert.Equal(1, result.Replaced);
        Assert.Equal(0, result.Skipped);

        var after = await target.Recipes.GetByIdAsync(id);
        Assert.Equal("Theirs", after!.Title);
    }

    [Fact]
    public async Task A_tie_can_be_kept_exactly_as_it_is_on_this_device()
    {
        using var source = new TestCookbook("source");
        using var target = new TestCookbook("target");

        var id = Guid.NewGuid();
        await target.Recipes.SaveAsync(new Recipe
        {
            Id = id, Title = "Mine", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await source.Recipes.SaveAsync(new Recipe
        {
            Id = id, Title = "Theirs", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });

        using var package = new MemoryStream();
        await source.Files.WriteAsync(package, new[] { id });
        package.Position = 0;

        var policy = new Policy(_ => ConflictDecision.KeepLocal);
        var result = await target.Files.ReadAsync(package, policy);

        Assert.Single(policy.Seen);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Replaced);

        var after = await target.Recipes.GetByIdAsync(id);
        Assert.Equal("Mine", after!.Title);
    }

    private sealed class Policy : ICookbookImportPolicy
    {
        private readonly Func<CookbookConflict, ConflictDecision> _decide;

        public Policy(Func<CookbookConflict, ConflictDecision> decide) => _decide = decide;

        public List<CookbookConflict> Seen { get; } = new();

        public Task<ConflictDecision> ResolveAsync(
            CookbookConflict conflict,
            CancellationToken cancellationToken = default)
        {
            Seen.Add(conflict);
            return Task.FromResult(_decide(conflict));
        }
    }

    [Fact]
    public async Task A_recipe_from_a_newer_format_is_refused_rather_than_half_read()
    {
        using var target = new TestCookbook();

        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("manifest.json");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("""{"format":"sedhep.cookbook","version":99,"recipeCount":0,"photoCount":0}""");
        }

        package.Position = 0;

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => target.Files.ReadAsync(package));
        Assert.Contains("newer version", error.Message);
    }

    [Fact]
    public async Task Something_that_is_not_a_cookbook_is_rejected()
    {
        using var target = new TestCookbook();

        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("notes.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("just a text file");
        }

        package.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => target.Files.ReadAsync(package));
    }
}

public sealed class AppSettingsTests
{
    [Fact]
    public void A_missing_settings_file_starts_from_defaults_and_is_written()
    {
        using var cookbook = new TestCookbook();

        var current = cookbook.Settings.Current;

        Assert.False(string.IsNullOrWhiteSpace(current.DeviceId));
        Assert.Equal("system", current.Theme);
        Assert.Equal(1.0, current.TextScale);
        Assert.True(File.Exists(Path.Combine(cookbook.Root, "settings.json")));
    }

    [Fact]
    public void Settings_survive_a_restart()
    {
        using var cookbook = new TestCookbook();

        cookbook.Settings.Current.DeviceName = "Rina's phone";
        cookbook.Settings.Current.Theme = "dark";
        cookbook.Settings.Current.TextScale = 1.12;
        cookbook.Settings.Save(cookbook.Settings.Current);

        var reloaded = new AppSettingsStore(
            Path.Combine(cookbook.Root, "settings.json"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AppSettingsStore>.Instance);

        Assert.Equal("Rina's phone", reloaded.Current.DeviceName);
        Assert.Equal("dark", reloaded.Current.Theme);
        Assert.Equal(1.12, reloaded.Current.TextScale);
        Assert.Equal(cookbook.Settings.Current.DeviceId, reloaded.Current.DeviceId);
    }
}

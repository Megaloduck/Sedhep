// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sedhep.Domain;

namespace Sedhep.Infrastructure;

/// <summary>
/// Writes and reads portable <c>.cookbook</c> packages: one zip per cookbook holding a manifest,
/// the recipes as JSON, and the photo files exactly as they are stored on the device.
/// </summary>
public sealed class CookbookFileService : ICookbookFileService
{
    /// <summary>The version written today. Readers must refuse versions they do not understand.</summary>
    public const int FormatVersion = 1;

    public const string FileExtension = ".cookbook";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IRecipeRepository _recipes;
    private readonly ICategoryRepository _categories;
    private readonly IRecipePhotoStorage _photos;
    private readonly IClock _clock;
    private readonly ILogger<CookbookFileService> _logger;

    public CookbookFileService(
        IRecipeRepository recipes,
        ICategoryRepository categories,
        IRecipePhotoStorage photos,
        IClock clock,
        ILogger<CookbookFileService> logger)
    {
        _recipes = recipes;
        _categories = categories;
        _photos = photos;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CookbookFileSummary> WriteAsync(
        Stream destination,
        IReadOnlyCollection<Guid>? recipeIds = null,
        CancellationToken cancellationToken = default)
    {
        var all = recipeIds is null ? await _recipes.GetAllAsync(cancellationToken) : null;
        var selected = new List<Recipe>();

        if (all is not null)
        {
            selected.AddRange(all);
        }
        else
        {
            foreach (var id in recipeIds!)
            {
                var recipe = await _recipes.GetByIdAsync(id, cancellationToken);
                if (recipe is not null) selected.Add(recipe);
            }
        }

        var photoCount = 0;

        using (var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            var payload = new CookbookPayload
            {
                Recipes = selected.Select(ToDto).ToList()
            };

            foreach (var recipe in selected)
            {
                foreach (var photo in recipe.Photos)
                {
                    if (!await _photos.ExistsAsync(recipe.Id.ToString(), photo.StorageId, cancellationToken))
                    {
                        _logger.LogWarning("Photo {StorageId} is missing while packaging a cookbook", photo.StorageId);
                        continue;
                    }

                    var entry = archive.CreateEntry(PhotoEntry(recipe.Id, photo.StorageId), CompressionLevel.NoCompression);
                    await using var target = entry.Open();
                    await using var source = await _photos.OpenReadAsync(
                        recipe.Id.ToString(), photo.StorageId, cancellationToken);
                    if (source is null)
                    {
                        continue;
                    }

                    await source.CopyToAsync(target, cancellationToken);
                    photoCount++;
                }
            }

            WriteJson(archive, "recipes.json", payload);
            WriteJson(archive, "manifest.json", new CookbookManifest
            {
                Format = "sedhep.cookbook",
                Version = FormatVersion,
                ExportedAt = _clock.UtcNow,
                RecipeCount = selected.Count,
                PhotoCount = photoCount
            });
        }

        var bytes = destination.CanSeek ? destination.Length : 0;
        _logger.LogInformation("Packaged {Recipes} recipes and {Photos} photos", selected.Count, photoCount);
        return new CookbookFileSummary(selected.Count, photoCount, bytes);
    }

    public async Task<CookbookImportResult> ReadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("This file is not a Sedhep cookbook.");
        var manifest = await ReadJson<CookbookManifest>(manifestEntry, cancellationToken)
            ?? throw new InvalidDataException("This cookbook file has no readable manifest.");

        if (manifest.Version > FormatVersion)
        {
            throw new InvalidDataException(
                $"This cookbook was written by a newer version of Sedhep (format {manifest.Version}).");
        }

        var recipesEntry = archive.GetEntry("recipes.json")
            ?? throw new InvalidDataException("This cookbook file contains no recipes.");
        var payload = await ReadJson<CookbookPayload>(recipesEntry, cancellationToken)
            ?? throw new InvalidDataException("This cookbook file contains unreadable recipes.");

        var warnings = new List<string>();
        var imported = 0;
        var replaced = 0;
        var photosRestored = 0;
        var skipped = 0;

        var categories = (await _categories.GetAllAsync(cancellationToken))
            .ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var dto in payload.Recipes)
        {
            var existing = await _recipes.GetByIdAsync(dto.Id, cancellationToken);
            if (existing is not null && existing.Revision > dto.Revision)
            {
                // The copy on this device is newer; the plan says never silently destroy it.
                skipped++;
                warnings.Add($"“{dto.Title}” was kept as it is, because this device has a newer version.");
                continue;
            }

            var categoryId = Guid.Empty;
            if (dto.CategoryId is { } incoming && !string.IsNullOrWhiteSpace(dto.CategoryName))
            {
                if (categories.TryGetValue(dto.CategoryName, out var match))
                {
                    categoryId = match;
                }
                else
                {
                    var created = new Category { Id = Guid.NewGuid(), Name = dto.CategoryName! };
                    await _categories.SaveAsync(created, cancellationToken);
                    categories[created.Name] = created.Id;
                    categoryId = created.Id;
                }
            }

            var recipe = new Recipe
            {
                Id = dto.Id,
                Title = dto.Title,
                Description = dto.Description,
                CategoryId = categoryId,
                Cuisine = dto.Cuisine,
                Servings = dto.Servings,
                PrepMinutes = dto.PrepMinutes,
                CookMinutes = dto.CookMinutes,
                IsFavorite = dto.IsFavorite,
                Notes = dto.Notes,
                Tags = dto.Tags,
                SourceDeviceId = string.IsNullOrWhiteSpace(dto.SourceDeviceId) ? dto.Id.ToString("N") : dto.SourceDeviceId,
                CreatedAt = dto.CreatedAt == default ? _clock.UtcNow : dto.CreatedAt,
                UpdatedAt = _clock.UtcNow,
                Revision = Math.Max(1, dto.Revision)
            };

            foreach (var ingredient in dto.Ingredients)
            {
                recipe.Ingredients.Add(new Ingredient
                {
                    Id = ingredient.Id == Guid.Empty ? Guid.NewGuid() : ingredient.Id,
                    RecipeId = recipe.Id,
                    SortOrder = ingredient.SortOrder,
                    Name = ingredient.Name,
                    Quantity = ingredient.Quantity,
                    Unit = ingredient.Unit
                });
            }

            foreach (var step in dto.Steps)
            {
                recipe.Steps.Add(new RecipeStep
                {
                    Id = step.Id == Guid.Empty ? Guid.NewGuid() : step.Id,
                    RecipeId = recipe.Id,
                    SortOrder = step.SortOrder,
                    Instruction = step.Instruction,
                    Minutes = step.Minutes
                });
            }

            foreach (var photo in dto.Photos)
            {
                var entry = archive.GetEntry(PhotoEntry(recipe.Id, photo.StorageId));
                if (entry is null)
                {
                    continue;
                }

                await using (var entryStream = entry.Open())
                {
                    await _photos.StoreAsync(recipe.Id.ToString(), photo.StorageId, entryStream, photo.Hash, cancellationToken);
                }

                recipe.Photos.Add(new RecipePhoto
                {
                    Id = photo.Id == Guid.Empty ? Guid.NewGuid() : photo.Id,
                    RecipeId = recipe.Id,
                    StorageId = photo.StorageId,
                    FileName = photo.FileName,
                    SizeBytes = photo.SizeBytes,
                    Hash = photo.Hash,
                    CapturedAt = photo.CapturedAt == default ? _clock.UtcNow : photo.CapturedAt,
                    IsCover = photo.IsCover
                });
                photosRestored++;
            }

            await _recipes.SaveAsync(recipe, cancellationToken);

            if (existing is null)
            {
                imported++;
            }
            else
            {
                replaced++;
            }
        }

        _logger.LogInformation(
            "Imported {Imported}, replaced {Replaced}, skipped {Skipped}, photos {Photos}",
            imported, replaced, skipped, photosRestored);

        return new CookbookImportResult(imported, replaced, photosRestored, skipped, warnings);
    }

    private static string PhotoEntry(Guid recipeId, string storageId)
        => $"photos/{recipeId:N}/{storageId}.jpg";

    private static void WriteJson<T>(ZipArchive archive, string name, T value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, value, JsonOptions);
    }

    private static async Task<T?> ReadJson<T>(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static RecipeDto ToDto(Recipe recipe) => new()
    {
        Id = recipe.Id,
        Title = recipe.Title,
        Description = recipe.Description,
        CategoryId = recipe.CategoryId == Guid.Empty ? null : recipe.CategoryId,
        CategoryName = recipe.CategoryName,
        Cuisine = recipe.Cuisine,
        Servings = recipe.Servings,
        PrepMinutes = recipe.PrepMinutes,
        CookMinutes = recipe.CookMinutes,
        IsFavorite = recipe.IsFavorite,
        Notes = recipe.Notes,
        Tags = recipe.Tags.ToList(),
        SourceDeviceId = recipe.SourceDeviceId,
        CreatedAt = recipe.CreatedAt,
        UpdatedAt = recipe.UpdatedAt,
        Revision = recipe.Revision,
        Ingredients = recipe.Ingredients
            .OrderBy(i => i.SortOrder)
            .Select(i => new IngredientDto
            {
                Id = i.Id,
                SortOrder = i.SortOrder,
                Name = i.Name,
                Quantity = i.Quantity,
                Unit = i.Unit
            }).ToList(),
        Steps = recipe.Steps
            .OrderBy(s => s.SortOrder)
            .Select(s => new StepDto
            {
                Id = s.Id,
                SortOrder = s.SortOrder,
                Instruction = s.Instruction,
                Minutes = s.Minutes
            }).ToList(),
        Photos = recipe.Photos
            .Select(p => new PhotoDto
            {
                Id = p.Id,
                StorageId = p.StorageId,
                FileName = p.FileName,
                SizeBytes = p.SizeBytes,
                Hash = p.Hash,
                CapturedAt = p.CapturedAt,
                IsCover = p.IsCover
            }).ToList()
    };

    private sealed class CookbookManifest
    {
        public string Format { get; set; } = "sedhep.cookbook";
        public int Version { get; set; } = FormatVersion;
        public DateTimeOffset ExportedAt { get; set; }
        public int RecipeCount { get; set; }
        public int PhotoCount { get; set; }
    }

    private sealed class CookbookPayload
    {
        public List<RecipeDto> Recipes { get; set; } = new();
    }

    private sealed class RecipeDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public Guid? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string Cuisine { get; set; } = "";
        public int Servings { get; set; }
        public int PrepMinutes { get; set; }
        public int CookMinutes { get; set; }
        public bool IsFavorite { get; set; }
        public string? Notes { get; set; }
        public List<string> Tags { get; set; } = new();
        public string SourceDeviceId { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int Revision { get; set; } = 1;
        public List<IngredientDto> Ingredients { get; set; } = new();
        public List<StepDto> Steps { get; set; } = new();
        public List<PhotoDto> Photos { get; set; } = new();
    }

    private sealed class IngredientDto
    {
        public Guid Id { get; set; }
        public int SortOrder { get; set; }
        public string Name { get; set; } = "";
        public string Quantity { get; set; } = "";
        public string Unit { get; set; } = "";
    }

    private sealed class StepDto
    {
        public Guid Id { get; set; }
        public int SortOrder { get; set; }
        public string Instruction { get; set; } = "";
        public int? Minutes { get; set; }
    }

    private sealed class PhotoDto
    {
        public Guid Id { get; set; }
        public string StorageId { get; set; } = "";
        public string FileName { get; set; } = "";
        public long SizeBytes { get; set; }
        public string? Hash { get; set; }
        public DateTimeOffset CapturedAt { get; set; }
        public bool IsCover { get; set; }
    }
}

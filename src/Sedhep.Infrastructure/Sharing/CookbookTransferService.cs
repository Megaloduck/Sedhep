// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sedhep.Domain;
using Microsoft.Extensions.Logging;

namespace Sedhep.Infrastructure;

public sealed partial class CookbookTransferService : ICookbookTransferService
{
    private readonly IRecipeRepository _recipes;
    private readonly IRecipePhotoStorage _photos;
    private readonly ILogger<CookbookTransferService> _logger;
    private readonly IClock _clock;

    public CookbookTransferService(
        IRecipeRepository recipes,
        IRecipePhotoStorage photos,
        ILogger<CookbookTransferService> logger,
        IClock clock)
    {
        _recipes = recipes;
        _photos = photos;
        _logger = logger;
        _clock = clock;
    }

    public async Task DeliverToLocalRepositoryAsync(
        string sourceDeviceId,
        string payloadId,
        Stream payload,
        string? payloadHash,
        DateTimeOffset offeredAt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "DeliverToLocalRepositoryAsync invoked: source={SourceDeviceId} payload={PayloadId} hash={PayloadHash}",
            sourceDeviceId,
            payloadId,
            payloadHash);

        using var ms = new MemoryStream();
        await payload.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        var doc = await JsonSerializer.DeserializeAsync<TransferPayload>(
            ms, TransferPayloadJsonContext.Default.TransferPayload, cancellationToken);
        if (doc is null)
        {
            _logger.LogWarning("Failed to deserialize incoming transfer payload.");
            return;
        }

        foreach (var incoming in doc.Recipes)
        {
            var recipe = new Recipe
            {
                Id = incoming.Id,
                Title = incoming.Title,
                Description = incoming.Description,
                CategoryId = incoming.CategoryId,
                Cuisine = incoming.Cuisine,
                Servings = incoming.Servings,
                PrepMinutes = incoming.PrepMinutes,
                CookMinutes = incoming.CookMinutes,
                SourceDeviceId = sourceDeviceId,
                Notes = incoming.Notes,
                CreatedAt = incoming.CreatedAt == default ? _clock.UtcNow : incoming.CreatedAt,
                UpdatedAt = _clock.UtcNow
            };

            foreach (var ingredient in incoming.Ingredients)
            {
                recipe.Ingredients.Add(new Ingredient
                {
                    Id = ingredient.Id,
                    RecipeId = recipe.Id,
                    SortOrder = ingredient.SortOrder,
                    Name = ingredient.Name,
                    Quantity = ingredient.Quantity,
                    Unit = ingredient.Unit
                });
            }

            foreach (var step in incoming.Steps)
            {
                recipe.Steps.Add(new RecipeStep
                {
                    Id = step.Id,
                    RecipeId = recipe.Id,
                    SortOrder = step.SortOrder,
                    Instruction = step.Instruction,
                    Minutes = step.Minutes
                });
            }

            foreach (var photo in incoming.Photos)
            {
                recipe.Photos.Add(new RecipePhoto
                {
                    Id = photo.Id,
                    RecipeId = recipe.Id,
                    StorageId = photo.StorageId,
                    FileName = photo.FileName,
                    SizeBytes = photo.SizeBytes,
                    Hash = photo.Hash,
                    CapturedAt = photo.CapturedAt,
                    IsCover = photo.IsCover
                });
            }

            await _recipes.SaveAsync(recipe, cancellationToken);

            foreach (var photo in recipe.Photos)
            {
                if (!string.IsNullOrEmpty(photo.StorageId))
                {
                    await _photos.ExistsAsync(recipe.Id.ToString(), photo.StorageId, cancellationToken);
                }
            }
        }
    }

    public async Task<Stream> ExportCookbookAsync(
        string exportTag,
        IReadOnlyCollection<string> recipeIds,
        IProgress<CookbookExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "ExportCookbookAsync invoked: tag={ExportTag} recipeCount={RecipeCount}",
            exportTag,
            recipeIds.Count);

        var recipes = new List<ExportRecipe>();
        long photoBytes = 0;

        foreach (var idText in recipeIds)
        {
            if (!Guid.TryParse(idText, out var recipeId)) continue;
            var recipe = await _recipes.GetByIdAsync(recipeId, cancellationToken);
            if (recipe is null) continue;

            var exportRecipe = new ExportRecipe
            {
                Id = recipe.Id,
                Title = recipe.Title,
                Description = recipe.Description,
                CategoryId = recipe.CategoryId,
                Cuisine = recipe.Cuisine,
                Servings = recipe.Servings,
                PrepMinutes = recipe.PrepMinutes,
                CookMinutes = recipe.CookMinutes,
                SourceDeviceId = recipe.SourceDeviceId,
                Notes = recipe.Notes,
                CreatedAt = recipe.CreatedAt,
                UpdatedAt = recipe.UpdatedAt
            };

            foreach (var ing in recipe.Ingredients)
            {
                exportRecipe.Ingredients.Add(new ExportIngredient
                {
                    Id = ing.Id,
                    RecipeId = ing.RecipeId,
                    SortOrder = ing.SortOrder,
                    Name = ing.Name,
                    Quantity = ing.Quantity,
                    Unit = ing.Unit
                });
            }

            foreach (var step in recipe.Steps)
            {
                exportRecipe.Steps.Add(new ExportStep
                {
                    Id = step.Id,
                    RecipeId = step.RecipeId,
                    SortOrder = step.SortOrder,
                    Instruction = step.Instruction,
                    Minutes = step.Minutes
                });
            }

            foreach (var photo in recipe.Photos)
            {
                exportRecipe.Photos.Add(new ExportRecipePhoto
                {
                    Id = photo.Id,
                    RecipeId = photo.RecipeId,
                    StorageId = photo.StorageId,
                    FileName = photo.FileName,
                    SizeBytes = photo.SizeBytes,
                    Hash = photo.Hash,
                    CapturedAt = photo.CapturedAt,
                    IsCover = photo.IsCover
                });
                photoBytes += photo.SizeBytes;
            }

            recipes.Add(exportRecipe);
        }

        var payload = new TransferPayload
        {
            Tag = exportTag,
            ExportedAt = _clock.UtcNow,
            Recipes = recipes
        };

        // The stream is handed to the caller, so this method must not dispose it.
        var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, payload, TransferPayloadJsonContext.Default.TransferPayload, cancellationToken);
        stream.Position = 0;

        progress?.Report(new CookbookExportProgress(
            exportTag,
            recipes.Count,
            photoBytes,
            recipes.Count,
            photoBytes));

        return stream;
    }

    private sealed class TransferPayload
    {
        public string Tag { get; set; } = "";
        public DateTimeOffset ExportedAt { get; set; }
        public List<ExportRecipe> Recipes { get; set; } = new();
    }

    private sealed class ExportRecipe
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public Guid CategoryId { get; set; }
        public string Cuisine { get; set; } = "";
        public int Servings { get; set; }
        public int PrepMinutes { get; set; }
        public int CookMinutes { get; set; }
        public string SourceDeviceId { get; set; } = "";
        public string? Notes { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public List<ExportIngredient> Ingredients { get; set; } = new();
        public List<ExportStep> Steps { get; set; } = new();
        public List<ExportRecipePhoto> Photos { get; set; } = new();
    }

    private sealed class ExportIngredient
    {
        public Guid Id { get; set; }
        public Guid RecipeId { get; set; }
        public int SortOrder { get; set; }
        public string Name { get; set; } = "";
        public string Quantity { get; set; } = "";
        public string Unit { get; set; } = "";
    }

    private sealed class ExportStep
    {
        public Guid Id { get; set; }
        public Guid RecipeId { get; set; }
        public int SortOrder { get; set; }
        public string Instruction { get; set; } = "";
        public int? Minutes { get; set; }
    }

    private sealed class ExportRecipePhoto
    {
        public Guid Id { get; set; }
        public Guid RecipeId { get; set; }
        public string StorageId { get; set; } = "";
        public string FileName { get; set; } = "";
        public long SizeBytes { get; set; }
        public string? Hash { get; set; }
        public DateTimeOffset CapturedAt { get; set; }
        public bool IsCover { get; set; }
    }

    [JsonSerializable(typeof(TransferPayload))]
    private sealed partial class TransferPayloadJsonContext : JsonSerializerContext
    {
    }
}

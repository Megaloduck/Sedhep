// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Sedhep.Domain;

/// <summary>
/// A recipe as it belongs to the cookbook. Everything here is stored on the device; nothing in this
/// model knows about accounts or servers.
/// </summary>
public sealed class Recipe
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public Guid CategoryId { get; set; }

    /// <summary>Filled in by the repository so lists never need a second lookup.</summary>
    public string? CategoryName { get; set; }

    public string Cuisine { get; set; } = "";

    public int Servings { get; set; }

    public int PrepMinutes { get; set; }

    public int CookMinutes { get; set; }

    public int TotalMinutes => PrepMinutes + CookMinutes;

    public bool IsFavorite { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Bumped on every save. Two devices comparing revisions is what makes conflict detection
    /// possible without trusting device clocks.
    /// </summary>
    public int Revision { get; set; } = 1;

    /// <summary>The device the recipe originally came from, so a transfer can say who sent it.</summary>
    public string SourceDeviceId { get; set; } = "";

    public string? Notes { get; set; }

    public IList<string> Tags { get; set; } = new List<string>();

    public ICollection<Ingredient> Ingredients { get; set; } = new List<Ingredient>();

    public ICollection<RecipeStep> Steps { get; set; } = new List<RecipeStep>();

    public ICollection<RecipePhoto> Photos { get; set; } = new List<RecipePhoto>();

    public RecipePhoto? CoverPhoto
    {
        get
        {
            foreach (var photo in Photos)
            {
                if (photo.IsCover)
                {
                    return photo;
                }
            }

            return null;
        }
    }
}

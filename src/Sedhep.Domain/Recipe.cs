// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Sedhep.Domain;

public sealed class Recipe
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string Cuisine { get; set; } = "";
    public int Servings { get; set; }
    public int PrepMinutes { get; set; }
    public int CookMinutes { get; set; }
    public int TotalMinutes => PrepMinutes + CookMinutes;
    public bool IsFavorite { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string SourceDeviceId { get; set; } = "";
    public string? Notes { get; set; }

    public ICollection<Ingredient> Ingredients { get; set; } = new List<Ingredient>();
    public ICollection<RecipeStep> Steps { get; set; } = new List<RecipeStep>();
    public ICollection<RecipePhoto> Photos { get; set; } = new List<RecipePhoto>();
}

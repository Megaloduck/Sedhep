// Licensed under the MIT license. See LICENSE in the project root.

using System;

namespace Sedhep.Domain;

public sealed class Ingredient
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public int SortOrder { get; set; }
    public string Name { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Unit { get; set; } = "";
}

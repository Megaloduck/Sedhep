// Licensed under the MIT license. See LICENSE in the project root.

using System;

namespace Sedhep.Domain;

public sealed class RecipeStep
{
    public Guid Id { get; set; }
    public Guid RecipeId { get; set; }
    public int SortOrder { get; set; }
    public string Instruction { get; set; } = "";
    public int? Minutes { get; set; }
}

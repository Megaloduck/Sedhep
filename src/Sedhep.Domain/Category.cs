// Licensed under the MIT license. See LICENSE in the project root.

using System;

namespace Sedhep.Domain;

public sealed class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

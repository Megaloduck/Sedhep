using System;

namespace Sedhep.ViewModels;

/// <summary>
/// A category as the user sees it in a picker. <see cref="Guid.Empty"/> stands for
/// "no category", which is also where recipes end up when their category is deleted.
/// </summary>
public sealed record CategoryOption(Guid Id, string Name);

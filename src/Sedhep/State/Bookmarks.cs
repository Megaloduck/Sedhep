// Licensed under the MIT license. See LICENSE in the project root.

using System.Collections.Concurrent;

namespace Sedhep.State;

internal sealed class Bookmarks
{
    private readonly ConcurrentDictionary<string, BookmarkEntry> _store = new();

    public void Record(string entityId, int? stepIndex, double scrollFraction)
    {
        _store[entityId] = new BookmarkEntry(entityId, stepIndex, scrollFraction, DateTimeOffset.UtcNow);
    }

    public BookmarkEntry? Lookup(string entityId) => _store.TryGetValue(entityId, out var entry) ? entry : null;

    public void Clear(string entityId) => _store.TryRemove(entityId, out _);

    public void ClearAll() => _store.Clear();
}

internal sealed record BookmarkEntry(
    string EntityId,
    int? StepIndex,
    double ScrollFraction,
    DateTimeOffset RecordedAt);

// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sedhep.Infrastructure;

public interface ICookbookEventSink
{
    void Publish(CookbookEvent e);
    Task SubscribeAsync(Action<CookbookEvent> handler, CancellationToken cancellationToken = default);
}

public sealed record CookbookEvent(
    string Kind,
    Guid? RecipeId,
    string? FromDeviceId,
    DateTimeOffset Timestamp);

public sealed class InMemoryCookbookEventSink : ICookbookEventSink, IAsyncDisposable
{
    private readonly List<Action<CookbookEvent>> _handlers = new();
    private readonly object _lock = new();

    public void Publish(CookbookEvent e)
    {
        lock (_lock)
        {
            foreach (var h in _handlers)
            {
                h(e);
            }
        }
    }

    public Task SubscribeAsync(Action<CookbookEvent> handler, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _handlers.Add(handler);
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            _handlers.Clear();
        }
        return default;
    }
}

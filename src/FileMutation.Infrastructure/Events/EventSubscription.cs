using FileMutation.Domain.Events;

namespace FileMutation.Infrastructure.Events;

/// <summary>One registered handler, seen by the dispatcher without knowing its event type.</summary>
internal interface IEventSubscription
{
    /// <summary>Gets the handler's type name, for logs and metric tags.</summary>
    string HandlerName { get; }

    /// <summary>Returns whether the event's exact runtime type is the one this subscription was closed over.</summary>
    bool CanHandle(BatchEvent batchEvent);

    /// <summary>Resolves the handler and invokes it with the event cast to its own type.</summary>
    ValueTask HandleAsync(IServiceProvider services, BatchEvent batchEvent, CancellationToken cancellationToken);
}

/// <summary>
/// The typed half of a subscription: the cast to <typeparamref name="TEvent"/> is compiled here, so the
/// dispatcher needs no reflection. Exact-type matching keeps a subclass from reaching its parent's handler.
/// </summary>
internal sealed class EventSubscription<TEvent>(
    string handlerName,
    Func<IServiceProvider, IEventHandler<TEvent>> resolveHandler) : IEventSubscription
    where TEvent : BatchEvent
{
    public string HandlerName { get; } = handlerName;

    public bool CanHandle(BatchEvent batchEvent) => batchEvent.GetType() == typeof(TEvent);

    public ValueTask HandleAsync(IServiceProvider services, BatchEvent batchEvent, CancellationToken cancellationToken) =>
        resolveHandler(services).HandleAsync((TEvent)batchEvent, cancellationToken);
}

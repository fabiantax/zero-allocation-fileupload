namespace FileMutation.Domain.Events;

/// <summary>Reacts to one event type. Runs on a background consumer, never on the request path.
/// A throwing handler is logged and counted; it cannot fail the upload or stop the host.</summary>
/// <typeparam name="TEvent">The event type this handler reacts to.</typeparam>
public interface IEventHandler<in TEvent> where TEvent : BatchEvent
{
    /// <summary>Handles one event.</summary>
    /// <param name="batchEvent">The event to handle.</param>
    /// <param name="cancellationToken">Signals that the host is stopping.</param>
    /// <returns>A task that completes when the event has been handled.</returns>
    ValueTask HandleAsync(TEvent batchEvent, CancellationToken cancellationToken);
}

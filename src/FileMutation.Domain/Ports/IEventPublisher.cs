using FileMutation.Domain.Events;

namespace FileMutation.Domain.Ports;

/// <summary>Hands batch events to the in-process consumer. Delivery is at-most-once and does not
/// survive a process crash (ADR-0012).</summary>
public interface IEventPublisher
{
    /// <summary>Queues an event without blocking and without throwing. Returns false only when a
    /// non-terminal event was dropped because the queue is at capacity; terminal events are
    /// always queued.</summary>
    /// <param name="batchEvent">The event to queue.</param>
    /// <returns><see langword="false"/> when a non-terminal event was dropped; otherwise <see langword="true"/>.</returns>
    bool TryPublish(BatchEvent batchEvent);
}

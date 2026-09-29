using FileMutation.Domain.Batches;

namespace FileMutation.Domain.Events;

/// <summary>Base of every batch event. <see cref="Sequence"/> starts at 1 per batch and has no gaps
/// at the source, so a consumer that sees a gap knows an event was dropped.</summary>
/// <param name="BatchId">The batch this event belongs to.</param>
/// <param name="Sequence">The 1-based position of this event within its batch.</param>
/// <param name="OccurredAt">When the event happened.</param>
public abstract record BatchEvent(BatchId BatchId, long Sequence, DateTimeOffset OccurredAt)
{
    /// <summary>Gets whether this event ends its batch. Terminal events are never dropped.</summary>
    public virtual bool IsTerminal => false;
}

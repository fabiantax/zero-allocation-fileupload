using FileMutation.Domain.Batches;

namespace FileMutation.Domain.Events;

/// <summary>Every file part was processed and the archive was completed.</summary>
/// <param name="BatchId">The batch this event belongs to.</param>
/// <param name="Sequence">The 1-based position of this event within its batch.</param>
/// <param name="OccurredAt">When the event happened.</param>
/// <param name="MutatedCount">How many files were mutated.</param>
/// <param name="RejectedCount">How many files were rejected.</param>
public sealed record BatchCompleted(BatchId BatchId, long Sequence, DateTimeOffset OccurredAt,
    int MutatedCount, int RejectedCount) : BatchEvent(BatchId, Sequence, OccurredAt)
{
    /// <inheritdoc />
    public override bool IsTerminal => true;
}

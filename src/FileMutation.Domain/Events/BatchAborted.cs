using FileMutation.Domain.Batches;

namespace FileMutation.Domain.Events;

/// <summary>The batch stopped early: client disconnect, malformed multipart, or a limit breach
/// after the response started.</summary>
/// <param name="BatchId">The batch this event belongs to.</param>
/// <param name="Sequence">The 1-based position of this event within its batch.</param>
/// <param name="OccurredAt">When the event happened.</param>
/// <param name="FilesProcessed">How many files were processed before the batch stopped.</param>
/// <param name="ReasonCode">Why the batch stopped.</param>
public sealed record BatchAborted(BatchId BatchId, long Sequence, DateTimeOffset OccurredAt,
    int FilesProcessed, string ReasonCode) : BatchEvent(BatchId, Sequence, OccurredAt)
{
    /// <inheritdoc />
    public override bool IsTerminal => true;
}

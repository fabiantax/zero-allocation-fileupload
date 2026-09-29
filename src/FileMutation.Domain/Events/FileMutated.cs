using FileMutation.Domain.Batches;

namespace FileMutation.Domain.Events;

/// <summary>One file was accepted and mutated. Not a delivery receipt: the client may still disconnect.</summary>
/// <param name="BatchId">The batch this event belongs to.</param>
/// <param name="Sequence">The 1-based position of this event within its batch.</param>
/// <param name="OccurredAt">When the event happened.</param>
/// <param name="FileIndex">The 0-based position of the file among the request's file parts.</param>
/// <param name="EntryName">The archive entry name chosen for the file.</param>
public sealed record FileMutated(BatchId BatchId, long Sequence, DateTimeOffset OccurredAt,
    int FileIndex, string EntryName) : BatchEvent(BatchId, Sequence, OccurredAt);

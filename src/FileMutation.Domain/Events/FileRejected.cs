using FileMutation.Domain.Batches;

namespace FileMutation.Domain.Events;

/// <summary>One file was rejected; the batch continues.</summary>
/// <param name="BatchId">The batch this event belongs to.</param>
/// <param name="Sequence">The 1-based position of this event within its batch.</param>
/// <param name="OccurredAt">When the event happened.</param>
/// <param name="FileIndex">The 0-based position of the file among the request's file parts.</param>
/// <param name="DeclaredFileName">The file name as sent by the caller; may be invalid.</param>
/// <param name="ReasonCode">Why the file was rejected.</param>
public sealed record FileRejected(BatchId BatchId, long Sequence, DateTimeOffset OccurredAt,
    int FileIndex, string? DeclaredFileName, string ReasonCode) : BatchEvent(BatchId, Sequence, OccurredAt);

using FileMutation.Domain.Batches;

namespace FileMutation.Domain.Events;

/// <summary>A chunk of files finished: every ChunkSize files, plus a final partial chunk.</summary>
/// <param name="BatchId">The batch this event belongs to.</param>
/// <param name="Sequence">The 1-based position of this event within its batch.</param>
/// <param name="OccurredAt">When the event happened.</param>
/// <param name="ChunkNumber">The 1-based number of the chunk that finished.</param>
/// <param name="FilesInChunk">How many files the chunk held.</param>
/// <param name="FilesSoFar">How many files the batch has processed including this chunk.</param>
public sealed record BatchChunkCompleted(BatchId BatchId, long Sequence, DateTimeOffset OccurredAt,
    int ChunkNumber, int FilesInChunk, int FilesSoFar) : BatchEvent(BatchId, Sequence, OccurredAt);

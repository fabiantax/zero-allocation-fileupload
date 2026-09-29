namespace FileMutation.Domain.Batches;

/// <summary>What happened to one file part of a batch.</summary>
public enum BatchFileStatus
{
    /// <summary>The file was accepted, mutated and written to the archive.</summary>
    Mutated,

    /// <summary>The file was not mutated; <see cref="BatchFileOutcome.ReasonCode"/> says why.</summary>
    Rejected,
}

/// <summary>One manifest row: metadata about one file part, never its content.</summary>
/// <param name="Index">0-based position among file parts in the request.</param>
/// <param name="DeclaredFileName">The file name as sent by the caller; may be invalid.</param>
/// <param name="EntryName">The archive entry name when mutated (deduplicated); otherwise <see langword="null"/>.</param>
/// <param name="Status">What happened to the file.</param>
/// <param name="ReasonCode">Why the file was rejected; <see langword="null"/> when mutated.</param>
public sealed record BatchFileOutcome(
    int Index,
    string? DeclaredFileName,
    string? EntryName,
    BatchFileStatus Status,
    string? ReasonCode);

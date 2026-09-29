using FileMutation.Domain.Batches;

namespace FileMutation.Api.Batches;

/// <summary>
/// The body of <c>manifest.json</c>, the last entry of every batch archive (ADR 0011). Rows are the
/// domain outcomes as they are: metadata only, never file content. Public so tests call it directly
/// (the repo forbids <c>InternalsVisibleTo</c>).
/// </summary>
/// <param name="BatchId">The batch identifier as 32 lowercase hex characters.</param>
/// <param name="Complete"><see langword="false"/> when the batch ended early.</param>
/// <param name="Mutated">How many rows have status mutated.</param>
/// <param name="Rejected">How many rows have status rejected.</param>
/// <param name="Files">One row per file part, in request order.</param>
public sealed record BatchManifest(
    string BatchId, bool Complete, int Mutated, int Rejected, IReadOnlyList<BatchFileOutcome> Files)
{
    /// <summary>Builds the manifest, counting rows by status so the counts cannot disagree with them.</summary>
    /// <param name="id">The batch identifier.</param>
    /// <param name="outcomes">One outcome per file part, in request order.</param>
    /// <param name="complete"><see langword="false"/> when the batch ended early.</param>
    /// <returns>The manifest.</returns>
    public static BatchManifest From(BatchId id, IReadOnlyList<BatchFileOutcome> outcomes, bool complete) =>
        new(
            id.Value,
            complete,
            outcomes.Count(outcome => outcome.Status == BatchFileStatus.Mutated),
            outcomes.Count(outcome => outcome.Status == BatchFileStatus.Rejected),
            outcomes);
}

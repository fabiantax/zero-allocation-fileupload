using FileMutation.Domain.Batches;

namespace FileMutation.Application.Batches;

/// <summary>The outcome of one file plus, when mutated, its pooled content.</summary>
public sealed class BatchFileResult : IAsyncDisposable
{
    private readonly FileMutationResult? _mutation;

    internal BatchFileResult(BatchFileOutcome outcome, FileMutationResult? mutation) =>
        (Outcome, _mutation) = (outcome, mutation);

    /// <summary>Gets the manifest row for this file.</summary>
    public BatchFileOutcome Outcome { get; }

    /// <summary>Gets the mutated content. Throws unless <see cref="Outcome"/> is <see cref="BatchFileStatus.Mutated"/>.</summary>
    public Stream Content => _mutation?.Content
        ?? throw new InvalidOperationException("Only a mutated file has content.");

    /// <summary>Returns pooled segments to their pool.</summary>
    public ValueTask DisposeAsync() => _mutation?.DisposeAsync() ?? ValueTask.CompletedTask;
}

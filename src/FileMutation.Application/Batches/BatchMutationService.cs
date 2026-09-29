using FileMutation.Domain.Batches;
using FileMutation.Domain.Ports;

namespace FileMutation.Application.Batches;

/// <summary>Per-batch limits, supplied by the host from configuration.</summary>
public sealed record BatchLimits(int MaxFiles, long MaxFileBytes, int ChunkSize);

/// <summary>Starts batch sessions. One session per request.</summary>
public interface IBatchMutationService
{
    /// <summary>Starts a batch and assigns its <see cref="BatchId"/>. Publishes nothing yet.</summary>
    BatchMutationSession Start(BatchLimits limits);
}

/// <summary>Composes <see cref="ISingleFileMutationService"/> into batches; never touches <see cref="IFileMutator"/>.</summary>
public sealed class BatchMutationService(
    ISingleFileMutationService files, IEventPublisher publisher, TimeProvider time) : IBatchMutationService
{
    /// <inheritdoc />
    public BatchMutationSession Start(BatchLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaxFileBytes);
        return new BatchMutationSession(BatchId.New(), limits, files, publisher, time);
    }
}

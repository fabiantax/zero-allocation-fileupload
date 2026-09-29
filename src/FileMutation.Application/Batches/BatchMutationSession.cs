using FileMutation.Domain.Batches;
using FileMutation.Domain.Events;
using FileMutation.Domain.Ports;

namespace FileMutation.Application.Batches;

/// <summary>
/// Drives one batch file by file. Holds outcome metadata only; file content is owned by the
/// returned <see cref="BatchFileResult"/> and released when it is disposed. One request drives
/// one session sequentially, so it is not thread-safe.
/// </summary>
public sealed class BatchMutationSession
{
    private readonly ISingleFileMutationService _files;
    private readonly IEventPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly BatchLimits _limits;
    private readonly List<BatchFileOutcome> _outcomes = [];
    private readonly HashSet<string> _entryNames = new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;

    internal BatchMutationSession(
        BatchId id, BatchLimits limits, ISingleFileMutationService files, IEventPublisher publisher, TimeProvider time)
    {
        (Id, _limits, _files, _publisher, _time) = (id, limits, files, publisher, time);
        Outcomes = _outcomes.AsReadOnly();
    }

    /// <summary>Gets the batch identifier.</summary>
    public BatchId Id { get; }

    /// <summary>Gets one outcome per file part seen so far, in request order.</summary>
    public IReadOnlyList<BatchFileOutcome> Outcomes { get; }

    /// <summary>
    /// Validates and mutates the next file through <see cref="ISingleFileMutationService"/>. Files
    /// beyond <see cref="BatchLimits.MaxFiles"/> are rejected with reason code
    /// <c>BatchFileLimitExceeded</c> without reading their content, and a file whose mutation
    /// throws becomes a <c>MutationFailed</c> row, so one bad file never costs the rest.
    /// Publishes <see cref="FileMutated"/> or <see cref="FileRejected"/>. Dispose the result before
    /// the next call: that is what keeps one file in memory at a time.
    /// </summary>
    /// <param name="content">The file part's body.</param>
    /// <param name="declaredFileName">The file name as sent by the caller.</param>
    /// <param name="declaredContentType">The content type as sent by the caller.</param>
    /// <param name="cancellationToken">Cancels the read; cancellation is not a rejected row.</param>
    /// <returns>The manifest row and, when mutated, the pooled content.</returns>
    public async Task<BatchFileResult> MutateNextAsync(
        Stream content, string? declaredFileName, string? declaredContentType, CancellationToken cancellationToken)
    {
        var index = _outcomes.Count;
        var result = index >= _limits.MaxFiles
            ? Rejected(index, declaredFileName, "BatchFileLimitExceeded")
            : await MutateFileAsync(index, content, declaredFileName, declaredContentType, cancellationToken)
                .ConfigureAwait(false);
        _outcomes.Add(result.Outcome);
        Publish(result.Outcome.Status == BatchFileStatus.Mutated
            ? new FileMutated(Id, ++_sequence, Now, index, result.Outcome.EntryName!)
            : new FileRejected(Id, ++_sequence, Now, index, declaredFileName, result.Outcome.ReasonCode!));

        return result;
    }

    private static BatchFileResult Rejected(int index, string? declaredFileName, string reasonCode) =>
        new(new BatchFileOutcome(index, declaredFileName, null, BatchFileStatus.Rejected, reasonCode), null);

    private DateTimeOffset Now => _time.GetUtcNow();

    private async Task<BatchFileResult> MutateFileAsync(
        int index, Stream content, string? declaredFileName, string? declaredContentType, CancellationToken cancellationToken)
    {
        FileMutationResult mutation;
        try
        {
            mutation = await _files.MutateAsync(
                content, declaredFileName, declaredContentType, _limits.MaxFileBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Rejected(index, declaredFileName, nameof(FileMutationFailureReason.MutationFailed));
        }

        if (!mutation.IsAccepted)
        {
            return Rejected(index, declaredFileName, (mutation.AcceptanceReason?.ToString() ?? mutation.FailureReason?.ToString())!);
        }

        var outcome = new BatchFileOutcome(
            index, declaredFileName, Deduplicate(mutation.FileName!.Value), BatchFileStatus.Mutated, null);
        return new BatchFileResult(outcome, mutation);
    }

    // The first name keeps itself; later ones become "a (2).txt", "a (3).txt". Every candidate is
    // checked against all names issued so far, so "a (2).txt" can never be handed out twice.
    private string Deduplicate(string name)
    {
        if (_entryNames.Add(name))
        {
            return name;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{stem} ({suffix++}){extension}";
        }
        while (!_entryNames.Add(candidate));
        return candidate;
    }

    // The sequence advances even when the publisher drops an event, so a consumer can see the gap.
    private void Publish(BatchEvent batchEvent) => _publisher.TryPublish(batchEvent);
}

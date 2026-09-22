using System.IO.Pipelines;
using FileMutation.Domain;
using FileMutation.Domain.Ports;

namespace FileMutation.Application;

/// <summary>Identifies which stage rejected or failed to mutate one file.</summary>
public enum FileMutationFailureReason
{
    /// <summary>The file stream was empty.</summary>
    Empty,

    /// <summary>The file stream exceeded the configured byte limit.</summary>
    TooLarge,

    /// <summary>The file stream could not be read.</summary>
    Unreadable,

    /// <summary>The file was rejected by <see cref="FileAcceptance"/>.</summary>
    Rejected,

    /// <summary>The accepted file could not be mutated.</summary>
    MutationFailed
}

/// <summary>The outcome of validating and mutating one file.</summary>
public sealed class FileMutationResult : IAsyncDisposable
{
    private readonly PipeReader? _mutatedContent;
    private bool _disposed;

    private FileMutationResult(
        FileName? fileName,
        FileMutationFailureReason? failureReason,
        FileRejectionReason? acceptanceReason,
        PipeReader? mutatedContent)
    {
        FileName = fileName;
        FailureReason = failureReason;
        AcceptanceReason = acceptanceReason;
        _mutatedContent = mutatedContent;
    }

    /// <summary>Gets the safe filename when mutation succeeded.</summary>
    public FileName? FileName { get; }

    /// <summary>Gets the failed stage when mutation did not succeed.</summary>
    public FileMutationFailureReason? FailureReason { get; }

    /// <summary>Gets the acceptance rule that rejected the file, if any.</summary>
    public FileRejectionReason? AcceptanceReason { get; }

    /// <summary>Gets whether the file was accepted and mutated.</summary>
    public bool IsAccepted => FileName is not null;

    /// <summary>Gets the pooled mutated content for an accepted result.</summary>
    public Stream Content
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _mutatedContent?.AsStream(leaveOpen: true)
                ?? throw new InvalidOperationException("Only an accepted result has mutated content.");
        }
    }

    internal static FileMutationResult Accepted(FileName fileName, PipeReader mutatedContent) =>
        new(fileName, null, null, mutatedContent);

    internal static FileMutationResult Failed(
        FileMutationFailureReason reason,
        FileRejectionReason? acceptanceReason = null) =>
        new(null, reason, acceptanceReason, null);

    /// <summary>Returns the pooled output segments to their pool.</summary>
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return _mutatedContent?.CompleteAsync() ?? ValueTask.CompletedTask;
    }
}

/// <summary>Validates and mutates one bounded file, independently of any transport.</summary>
public sealed class SingleFileMutationService(IFileMutator fileMutator)
{
    /// <summary>Reads, validates, and mutates one file, returning success or a per-file failure.</summary>
    public async Task<FileMutationResult> MutateAsync(
        Stream content,
        string? declaredFileName,
        string? declaredContentType,
        long maxFileBytes,
        CancellationToken cancellationToken = default)
    {
        var read = await PooledFileContentReader.ReadAsync(
            content, maxFileBytes, cancellationToken).ConfigureAwait(false);
        if (read.Content is null)
        {
            return FileMutationResult.Failed(read.Failure!.Value);
        }

        await using var bufferedContent = read.Content;
        var bufferedBytes = bufferedContent.Content;
        var acceptance = FileAcceptance.Evaluate(
            in bufferedBytes, declaredFileName, declaredContentType);
        if (!acceptance.IsAccepted)
        {
            return FileMutationResult.Failed(
                FileMutationFailureReason.Rejected, acceptance.RejectionReason!.Value);
        }

        var output = new Pipe(new PipeOptions(
            minimumSegmentSize: FileMutationConstants.SegmentSize,
            pauseWriterThreshold: long.MaxValue,
            resumeWriterThreshold: long.MaxValue - 1));
        FileMutationResult? result = null;
        var outputReleased = false;
        try
        {
            await fileMutator.MutateAsync(
                bufferedContent.OpenReadStream(),
                output.Writer.AsStream(leaveOpen: true),
                cancellationToken).ConfigureAwait(false);
            await output.Writer.CompleteAsync().ConfigureAwait(false);
            result = FileMutationResult.Accepted(acceptance.FileName!, output.Reader);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await output.Writer.CompleteAsync(exception).ConfigureAwait(false);
            outputReleased = true;
            await output.Reader.CompleteAsync().ConfigureAwait(false);
            return FileMutationResult.Failed(FileMutationFailureReason.MutationFailed);
        }
        finally
        {
            if (result is null && !outputReleased)
            {
                await output.Writer.CompleteAsync().ConfigureAwait(false);
                await output.Reader.CompleteAsync().ConfigureAwait(false);
            }
        }
    }
}

using System.Buffers;
using System.IO.Pipelines;
using FileMutation.Domain;
using FileMutation.Domain.Ports;

namespace FileMutation.Infrastructure;

/// <summary>
/// Streams a file unchanged, then appends <c>\nyyyy-MM-dd:sequence</c> using the domain policy.
/// See <see href="../../docs/adr/0005-streaming-allocation-strategy.md">ADR 0005</see>.
/// </summary>
internal sealed class DateAndRandomSequenceMutator : IFileMutator
{
    /// <summary>The stable number of random characters appended to each file.</summary>
    public const int RandomSequenceLength = 16;

    private readonly TimeProvider _timeProvider;
    private readonly IRandomSequenceGenerator _randomSequenceGenerator;
    private readonly MemoryPool<byte> _memoryPool;

    /// <summary>Creates a streaming mutator with injected sources of time and randomness.</summary>
    public DateAndRandomSequenceMutator(
        TimeProvider timeProvider,
        IRandomSequenceGenerator randomSequenceGenerator,
        MemoryPool<byte>? memoryPool = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(randomSequenceGenerator);

        _timeProvider = timeProvider;
        _randomSequenceGenerator = randomSequenceGenerator;
        _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;
    }

    /// <summary>Copies <paramref name="source"/> asynchronously and appends the mutation suffix.</summary>
    public async ValueTask MutateAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var reader = PipeReader.Create(
            source,
            new StreamPipeReaderOptions(
                pool: _memoryPool,
                bufferSize: FileMutationConstants.SegmentSize,
                minimumReadSize: FileMutationConstants.SegmentSize,
                leaveOpen: true));
        var writer = PipeWriter.Create(
            destination,
            new StreamPipeWriterOptions(
                pool: _memoryPool,
                minimumBufferSize: FileMutationConstants.SegmentSize,
                leaveOpen: true));
        try
        {
            await CopySourceAsync(reader, writer, cancellationToken).ConfigureAwait(false);
            AppendSuffix(writer);
            await FlushAsync(writer, cancellationToken).ConfigureAwait(false);
            await reader.CompleteAsync().ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Complete both pipes with the failure so their owners see it, then let it propagate.
            try
            {
                await reader.CompleteAsync(exception).ConfigureAwait(false);
            }
            finally
            {
                await writer.CompleteAsync(exception).ConfigureAwait(false);
            }

            throw;
        }
    }

    private static async ValueTask CopySourceAsync(
        PipeReader reader,
        PipeWriter writer,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            if (result.IsCanceled)
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
                throw new OperationCanceledException(cancellationToken);
            }

            WriteBuffer(buffer, writer);
            reader.AdvanceTo(buffer.End);
            await FlushAsync(writer, cancellationToken).ConfigureAwait(false);

            if (result.IsCompleted)
            {
                return;
            }
        }
    }

    internal static void WriteBuffer(in ReadOnlySequence<byte> buffer, PipeWriter writer)
    {
        foreach (var segment in buffer)
        {
            var remaining = segment.Span;
            while (!remaining.IsEmpty)
            {
                var bytesToCopy = Math.Min(remaining.Length, FileMutationConstants.SegmentSize);
                remaining[..bytesToCopy].CopyTo(writer.GetSpan(bytesToCopy));
                writer.Advance(bytesToCopy);
                remaining = remaining[bytesToCopy..];
            }
        }
    }

    private void AppendSuffix(PipeWriter writer)
    {
        Span<char> randomSequence = stackalloc char[RandomSequenceLength];
        _randomSequenceGenerator.Fill(randomSequence);

        var context = new MutationContext(_timeProvider.GetUtcNow(), randomSequence);
        var requiredByteCount = MutationPolicy.GetRequiredByteCount(in context);
        var destination = writer.GetSpan(requiredByteCount);
        if (!MutationPolicy.TryWriteSuffix(destination, in context, out var bytesWritten))
        {
            throw new InvalidOperationException("The mutation policy rejected a correctly sized destination.");
        }

        writer.Advance(bytesWritten);
    }

    private static async ValueTask FlushAsync(PipeWriter writer, CancellationToken cancellationToken)
    {
        var result = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsCanceled)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }
}

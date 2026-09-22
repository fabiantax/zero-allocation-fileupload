using System.Buffers;
using System.IO.Pipelines;

namespace FileMutation.Application;

/// <summary>One file held in pooled pipe segments until its mutation has completed.</summary>
public sealed class BufferedFileContent : IAsyncDisposable
{
    private readonly PipeReader _reader;
    private ReadOnlySequence<byte> _content;
    private bool _contentOpened;

    internal BufferedFileContent(PipeReader reader, in ReadOnlySequence<byte> content)
    {
        _reader = reader;
        _content = content;
    }

    /// <summary>Gets the complete file content without copying it into one array.</summary>
    internal ReadOnlySequence<byte> Content =>
        _contentOpened ? throw new ObjectDisposedException(nameof(BufferedFileContent)) : _content;

    /// <summary>Returns a stream over the buffered content and marks it as consumed.</summary>
    public Stream OpenReadStream()
    {
        ObjectDisposedException.ThrowIf(_contentOpened, this);
        _contentOpened = true;
        _reader.AdvanceTo(_content.Start, _content.End);
        return _reader.AsStream(leaveOpen: true);
    }

    /// <summary>Returns the pooled segments to their pool.</summary>
    public ValueTask DisposeAsync() => _reader.CompleteAsync();
}

/// <summary>Reads one bounded file stream into pooled memory, for later validation.</summary>
public static class PooledFileContentReader
{
    private const int SegmentSize = 16 * 1024;

    /// <summary>Reads <paramref name="source"/> completely while enforcing the file byte limit.</summary>
    public static async Task<(BufferedFileContent? Content, FileMutationFailureReason? Failure)> ReadAsync(
        Stream source,
        long maxFileBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxFileBytes, 0);

        var pipe = new Pipe(new PipeOptions(
            minimumSegmentSize: SegmentSize,
            pauseWriterThreshold: maxFileBytes + 1,
            resumeWriterThreshold: maxFileBytes));
        var sourceReader = PipeReader.Create(
            source, new StreamPipeReaderOptions(bufferSize: SegmentSize, leaveOpen: true));
        var completed = false;
        long byteCount = 0;

        try
        {
            while (true)
            {
                var read = await sourceReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                byteCount += read.Buffer.Length;
                if (byteCount > maxFileBytes)
                {
                    sourceReader.AdvanceTo(read.Buffer.Start, read.Buffer.End);
                    return (null, FileMutationFailureReason.TooLarge);
                }

                foreach (var segment in read.Buffer)
                {
                    pipe.Writer.Write(segment.Span);
                }

                sourceReader.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted)
                {
                    break;
                }
            }

            await pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await pipe.Writer.CompleteAsync().ConfigureAwait(false);
            var buffered = await pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (buffered.Buffer.IsEmpty)
            {
                return (null, FileMutationFailureReason.Empty);
            }

            completed = true;
            return (new BufferedFileContent(pipe.Reader, buffered.Buffer), null);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return (null, FileMutationFailureReason.Unreadable);
        }
        finally
        {
            await sourceReader.CompleteAsync().ConfigureAwait(false);
            await pipe.Writer.CompleteAsync().ConfigureAwait(false);
            if (!completed)
            {
                await pipe.Reader.CompleteAsync().ConfigureAwait(false);
            }
        }
    }
}

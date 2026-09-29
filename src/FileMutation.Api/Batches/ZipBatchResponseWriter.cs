using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using FileMutation.Domain.Batches;

namespace FileMutation.Api.Batches;

/// <summary>
/// Writes a batch as a ZIP archive onto a response body, one entry at a time, ending with
/// <c>manifest.json</c>. The body only ever sees asynchronous writes: it is non-seekable and Kestrel
/// throws on synchronous IO. One writer serves one request. Public so tests call it directly
/// (the repo forbids <c>InternalsVisibleTo</c>).
/// </summary>
/// <remarks>
/// In create mode <c>ZipArchiveEntry.OpenAsync</c> returns a stream whose <c>DisposeAsync</c> is the
/// synchronous <c>Dispose</c> (.NET 10.0.12), so closing an entry writes its last Deflate block and data
/// descriptor synchronously. Those bytes go to <see cref="DeferredWriteStream"/>, which holds them and
/// sends them to the body asynchronously the moment the entry is closed.
/// </remarks>
public sealed class ZipBatchResponseWriter : IAsyncDisposable
{
    private const string ManifestEntryName = "manifest.json";

    private readonly DeferredWriteStream _archiveStream;
    private readonly ZipArchive _archive;
    private bool _manifestWritten;

    private ZipBatchResponseWriter(DeferredWriteStream archiveStream, ZipArchive archive) =>
        (_archiveStream, _archive) = (archiveStream, archive);

    /// <summary>Starts an archive on the response body, which is left open when the writer is disposed.</summary>
    /// <param name="responseBody">A writable stream; it need not be seekable.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>The writer.</returns>
    public static async ValueTask<ZipBatchResponseWriter> CreateAsync(Stream responseBody, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(responseBody);
        var archiveStream = new DeferredWriteStream(responseBody);
        var archive = await ZipArchive.CreateAsync(
            archiveStream, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken: ct);
        return new ZipBatchResponseWriter(archiveStream, archive);
    }

    /// <summary>Writes one file as the next entry, completely, before returning.</summary>
    /// <param name="entryName">The entry name; validated and deduplicated by the caller.</param>
    /// <param name="content">The file content, read from its current position to its end.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when the entry is on the wire.</returns>
    /// <exception cref="InvalidOperationException">The manifest was written; it must stay last.</exception>
    public async ValueTask WriteEntryAsync(string entryName, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        ThrowIfManifestWritten();
        await WriteAsync(entryName, entryStream => content.CopyToAsync(entryStream, ct), ct);
    }

    /// <summary>Writes <c>manifest.json</c> as the final entry.</summary>
    /// <param name="id">The batch identifier.</param>
    /// <param name="outcomes">One outcome per file part, in request order.</param>
    /// <param name="complete"><see langword="false"/> when the batch ended early.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when the manifest is on the wire.</returns>
    /// <exception cref="InvalidOperationException">The manifest was already written.</exception>
    public async ValueTask WriteManifestAsync(
        BatchId id, IReadOnlyList<BatchFileOutcome> outcomes, bool complete, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        ThrowIfManifestWritten();
        _manifestWritten = true;
        var manifest = BatchManifest.From(id, outcomes, complete);
        await WriteAsync(
            ManifestEntryName,
            entryStream => JsonSerializer.SerializeAsync(
                entryStream, manifest, BatchManifestJsonContext.Default.BatchManifest, ct),
            ct);
    }

    /// <summary>Writes the central directory asynchronously. The response body is not closed.</summary>
    /// <returns>A task that completes when the archive is finished.</returns>
    public ValueTask DisposeAsync() => _archive.DisposeAsync();

    private async ValueTask WriteAsync(string entryName, Func<Stream, Task> writeContent, CancellationToken ct)
    {
        await using (var entryStream = await _archive.CreateEntry(entryName, CompressionLevel.Fastest).OpenAsync(ct))
        {
            await writeContent(entryStream);
        }

        // Closing the entry parked its tail in memory. Send it now, so a finished file reaches the caller
        // while later files are still being read.
        await _archiveStream.DrainAsync(ct);
    }

    private void ThrowIfManifestWritten()
    {
        if (_manifestWritten)
        {
            throw new InvalidOperationException($"{ManifestEntryName} is the last entry; nothing can follow it.");
        }
    }

    /// <summary>
    /// Write-only, non-seekable stream between the archive and the body. Asynchronous writes pass through;
    /// synchronous ones (an entry being closed) are held until <see cref="DrainAsync"/>. What is held is one
    /// entry's tail, bounded by the compressor's internal state and not by entry size, never the archive.
    /// </summary>
    private sealed class DeferredWriteStream(Stream body) : Stream
    {
        private readonly ArrayBufferWriter<byte> _pending = new();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public async ValueTask DrainAsync(CancellationToken cancellationToken)
        {
            if (_pending.WrittenCount > 0)
            {
                await body.WriteAsync(_pending.WrittenMemory, cancellationToken);
                _pending.ResetWrittenCount();
            }
        }

        public override void Write(byte[] buffer, int offset, int count) => _pending.Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => _pending.Write(buffer);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await DrainAsync(cancellationToken);
            await body.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => DrainAsync(cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

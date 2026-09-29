using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileMutation.Api.Batches;
using FileMutation.Domain.Batches;
using Xunit;

namespace FileMutation.Api.Tests;

/// <summary>
/// Tests for the streamed ZIP writer, which is public so they call it directly. Every archive goes through
/// <see cref="AsyncOnlyStream"/>, which behaves like a Kestrel response body with synchronous IO disabled:
/// not seekable, and any synchronous write, flush or dispose throws. A plain <see cref="MemoryStream"/>
/// would hide a synchronous write in <c>Dispose</c>, which is where the central directory is written.
/// </summary>
public sealed class ZipBatchResponseWriterTests
{
    private static readonly BatchId Id = ParseId("3f9c00112233445566778899aabbccdd");

    private static readonly BatchFileOutcome[] Rejection =
        [new(0, "b.json", null, BatchFileStatus.Rejected, "UnsupportedFileExtension")];

    [Fact]
    public void The_test_stream_rejects_synchronous_write_flush_and_dispose()
    {
        // Guards the guard: if this stream stopped throwing, every other test here would pass for nothing.
        var body = new AsyncOnlyStream(new MemoryStream());

        Assert.Throws<InvalidOperationException>(() => body.Write(new byte[1], 0, 1));
        Assert.Throws<InvalidOperationException>(() => body.Write(new byte[1].AsSpan()));
        Assert.Throws<InvalidOperationException>(() => body.WriteByte(1));
        Assert.Throws<InvalidOperationException>(body.Flush);
        Assert.Throws<InvalidOperationException>(body.Dispose);
        Assert.Equal(5, body.SynchronousAttempts);
        Assert.False(body.CanSeek);
    }

    [Fact]
    public async Task A_three_entry_archive_opens_with_ZipFile_OpenRead_in_write_order_with_the_manifest_last()
    {
        var bytes = await WriteArchiveAsync(async writer =>
        {
            await writer.WriteEntryAsync("a.txt", Utf8("alpha"), CancellationToken.None);
            await writer.WriteEntryAsync("b.txt", Utf8("bravo"), CancellationToken.None);
            await writer.WriteEntryAsync("c.txt", Utf8("charlie"), CancellationToken.None);
            await writer.WriteManifestAsync(Id, [], complete: true, CancellationToken.None);
        });

        Assert.Equal(["a.txt", "b.txt", "c.txt", "manifest.json"], ReadArchive(bytes, EntryNames));
    }

    [Fact]
    public async Task Entry_content_round_trips_byte_for_byte_and_is_deflated()
    {
        var large = new byte[300_000];
        new Random(20260929).NextBytes(large);
        var text = Encoding.UTF8.GetBytes("h\u00e9llo \u2713 \U0001F600\r\nline two\n");
        var repetitive = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("compress me ", 4000)));

        var bytes = await WriteArchiveAsync(async writer =>
        {
            await writer.WriteEntryAsync("text.txt", new MemoryStream(text), CancellationToken.None);
            await writer.WriteEntryAsync("empty.txt", new MemoryStream(), CancellationToken.None);
            await writer.WriteEntryAsync("large.txt", new TrickleStream(large, chunkSize: 4093), CancellationToken.None);
            await writer.WriteEntryAsync("repetitive.txt", new MemoryStream(repetitive), CancellationToken.None);
            await writer.WriteManifestAsync(Id, [], complete: true, CancellationToken.None);
        });

        ReadArchive(bytes, archive =>
        {
            Assert.Equal(text, ReadEntry(archive, "text.txt"));
            Assert.Empty(ReadEntry(archive, "empty.txt"));
            Assert.Equal(large, ReadEntry(archive, "large.txt"));
            Assert.Equal(repetitive, ReadEntry(archive, "repetitive.txt"));
            Assert.All(
                archive.Entries.Where(entry => entry.Name is "repetitive.txt" or "manifest.json"),
                entry => Assert.True(entry.CompressedLength < entry.Length, $"{entry.FullName} was stored"));
            return 0;
        });
    }

    [Fact]
    public async Task The_manifest_entry_is_the_serialized_manifest_of_the_outcomes_it_was_given()
    {
        // The field-by-field contract is pinned in BatchManifestTests; this pins that the writer emits that JSON.
        BatchFileOutcome[] outcomes =
        [
            new(0, "a.txt", "a.txt", BatchFileStatus.Mutated, null),
            new(1, "b.json", null, BatchFileStatus.Rejected, "UnsupportedFileExtension"),
        ];

        var bytes = await WriteArchiveAsync(writer =>
            writer.WriteManifestAsync(Id, outcomes, complete: true, CancellationToken.None).AsTask());

        var expected = JsonSerializer.SerializeToUtf8Bytes(
            BatchManifest.From(Id, outcomes, complete: true), BatchManifestJsonContext.Default.BatchManifest);
        Assert.Equal(expected, ReadArchive(bytes, archive => ReadEntry(archive, "manifest.json")));
    }

    [Fact]
    public async Task A_manifest_written_after_an_abort_says_complete_false_and_still_ends_the_archive()
    {
        var bytes = await WriteArchiveAsync(async writer =>
        {
            await writer.WriteEntryAsync("a.txt", Utf8("one"), CancellationToken.None);
            await writer.WriteManifestAsync(Id, Rejection, complete: false, CancellationToken.None);
        });

        var (names, manifest) = ReadArchive(
            bytes, archive => (EntryNames(archive), JsonNode.Parse(ReadEntry(archive, "manifest.json"))!));

        Assert.Equal(["a.txt", "manifest.json"], names);
        Assert.False(manifest["complete"]!.GetValue<bool>());
        Assert.Equal(1, manifest["rejected"]!.GetValue<int>());
    }

    [Fact]
    public async Task Nothing_can_follow_the_manifest()
    {
        using var sink = new MemoryStream();
        var writer = await ZipBatchResponseWriter.CreateAsync(new AsyncOnlyStream(sink), CancellationToken.None);
        await writer.WriteManifestAsync(Id, [], complete: true, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await writer.WriteEntryAsync("late.txt", Utf8("late"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await writer.WriteManifestAsync(Id, [], complete: false, CancellationToken.None));

        await writer.DisposeAsync();
        Assert.Equal(["manifest.json"], ReadArchive(sink.ToArray(), EntryNames));
    }

    [Fact]
    public async Task A_finished_entry_is_completely_on_the_wire_before_the_next_one_starts()
    {
        // The point of streaming: the first file reaches the caller while later files are still being read.
        // An entry is complete on the wire once its data descriptor (PK 07 08, written when the entry is
        // closed) has been sent, so that is what is counted after each entry and before the archive ends.
        using var sink = new MemoryStream();
        var writer = await ZipBatchResponseWriter.CreateAsync(new AsyncOnlyStream(sink), CancellationToken.None);

        await writer.WriteEntryAsync("a.txt", Utf8("alpha"), CancellationToken.None);
        var afterFirst = CountDataDescriptors(sink.ToArray());
        await writer.WriteEntryAsync("b.txt", Utf8("bravo"), CancellationToken.None);
        var afterSecond = CountDataDescriptors(sink.ToArray());
        var lengthBeforeDispose = sink.Length;
        await writer.DisposeAsync();

        Assert.Equal((1, 2), (afterFirst, afterSecond));
        Assert.True(sink.Length > lengthBeforeDispose, "the central directory was not written on dispose");
    }

    [Fact]
    public async Task A_large_entry_streams_to_the_body_in_small_writes_while_it_is_still_being_read()
    {
        // When the last byte of a 2 MiB incompressible entry has been read, nearly all of it must already be
        // on the wire, and no single write may be entry-sized. Buffering the entry or the archive fails both.
        var content = new byte[2 * 1_048_576];
        new Random(20260929).NextBytes(content);
        using var sink = new MemoryStream();
        var body = new AsyncOnlyStream(sink);
        var writer = await ZipBatchResponseWriter.CreateAsync(body, CancellationToken.None);
        long onWireAtEndOfInput = -1;
        var source = new TrickleStream(content, chunkSize: 16_384) { ReachedEnd = () => onWireAtEndOfInput = sink.Length };

        await writer.WriteEntryAsync("big.txt", source, CancellationToken.None);
        await writer.DisposeAsync();

        Assert.True(onWireAtEndOfInput > content.Length * 0.9, $"only {onWireAtEndOfInput} bytes were on the wire");
        Assert.True(body.LargestWrite < 512 * 1024, $"a single write of {body.LargestWrite} bytes was held back");
    }

    private static async Task<byte[]> WriteArchiveAsync(Func<ZipBatchResponseWriter, Task> script)
    {
        using var sink = new MemoryStream();
        var body = new AsyncOnlyStream(sink);
        var writer = await ZipBatchResponseWriter.CreateAsync(body, CancellationToken.None);
        await script(writer);
        await writer.DisposeAsync();

        Assert.Equal(0, body.SynchronousAttempts);
        Assert.False(body.DisposedAsynchronously, "the writer closed a response body it does not own");
        return sink.ToArray();
    }

    // ZipFile.OpenRead needs a file: it is the API a consumer of the archive uses, so the test uses it too.
    private static T ReadArchive<T>(byte[] bytes, Func<ZipArchive, T> read)
    {
        var path = Path.Combine(Path.GetTempPath(), $"zip-batch-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(path, bytes);
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return read(archive);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string[] EntryNames(ZipArchive archive) => archive.Entries.Select(entry => entry.FullName).ToArray();

    private static byte[] ReadEntry(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static int CountDataDescriptors(byte[] archive)
    {
        ReadOnlySpan<byte> signature = [0x50, 0x4B, 0x07, 0x08];
        var count = 0;
        for (var offset = 0; offset <= archive.Length - signature.Length; offset++)
        {
            count += archive.AsSpan(offset, signature.Length).SequenceEqual(signature) ? 1 : 0;
        }

        return count;
    }

    private static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));

    private static BatchId ParseId(string text)
    {
        Assert.True(BatchId.TryParse(text, out var id));
        return id;
    }

    /// <summary>
    /// A write-only, non-seekable stream that throws on any synchronous write, flush or dispose, as a Kestrel
    /// response body does with <c>AllowSynchronousIO = false</c>. Attempts are counted too, so a library that
    /// catches the exception and carries on is still caught.
    /// </summary>
    private sealed class AsyncOnlyStream(Stream inner) : Stream
    {
        private int _synchronousAttempts;

        public int SynchronousAttempts => _synchronousAttempts;

        public int LargestWrite { get; private set; }

        public bool DisposedAsynchronously { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw Synchronous(nameof(Write));

        public override void Write(ReadOnlySpan<byte> buffer) => throw Synchronous(nameof(Write));

        public override void WriteByte(byte value) => throw Synchronous(nameof(WriteByte));

        public override void Flush() => throw Synchronous(nameof(Flush));

        protected override void Dispose(bool disposing) => _ = disposing ? throw Synchronous(nameof(Dispose)) : 0;

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            LargestWrite = Math.Max(LargestWrite, buffer.Length);
            return inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override ValueTask DisposeAsync()
        {
            DisposedAsynchronously = true;
            return ValueTask.CompletedTask;
        }

        private InvalidOperationException Synchronous(string operation)
        {
            Interlocked.Increment(ref _synchronousAttempts);
            return new InvalidOperationException($"Synchronous {operation} is disallowed on the response body.");
        }
    }

    /// <summary>A read-only stream that returns at most <c>chunkSize</c> bytes per read, like a network body.</summary>
    private sealed class TrickleStream(byte[] data, int chunkSize) : Stream
    {
        private int _position;

        public Action? ReachedEnd { get; init; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, chunkSize), data.Length - _position);
            data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            if (count == 0)
            {
                ReachedEnd?.Invoke();
            }

            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

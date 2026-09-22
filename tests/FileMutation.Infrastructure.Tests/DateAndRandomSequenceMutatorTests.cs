using System.Buffers;
using System.IO.Pipelines;
using FileMutation.TestCommon;
using Xunit;

namespace FileMutation.Infrastructure.Tests;

public sealed class DateAndRandomSequenceMutatorTests
{
    private const string Suffix = "\n2026-09-22:0123456789ABCDEF";

    [Fact]
    public async Task Empty_file_contains_only_the_stable_suffix()
    {
        var output = await MutateAsync([]);

        Assert.Equal(Suffix, System.Text.Encoding.UTF8.GetString(output));
    }

    [Fact]
    public async Task Single_byte_file_is_preserved_before_the_suffix()
    {
        var output = await MutateAsync("x"u8.ToArray());

        Assert.Equal($"x{Suffix}", System.Text.Encoding.UTF8.GetString(output));
    }

    [Fact]
    public async Task SegmentBoundary_multi_segment_buffer_copies_every_segment()
    {
        var first = new BufferSegment("first"u8.ToArray());
        var second = first.Append("-second"u8.ToArray());
        var last = second.Append("-third"u8.ToArray());
        var input = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        var output = new Pipe();

        Assert.False(input.IsSingleSegment);
        DateAndRandomSequenceMutator.WriteBuffer(input, output.Writer);
        await output.Writer.CompleteAsync();

        var result = await output.Reader.ReadAsync();
        Assert.True(result.Buffer.ToArray().AsSpan().SequenceEqual("first-second-third"u8));
        output.Reader.AdvanceTo(result.Buffer.End);
        await output.Reader.CompleteAsync();
    }

    [Fact]
    public async Task SegmentBoundary_content_is_preserved_on_both_sides()
    {
        var input = new byte[(16 * 1024) + 1];
        input.AsSpan().Fill((byte)'a');
        input[^1] = (byte)'z';

        var output = await MutateAsync(input);

        Assert.True(output.AsSpan(0, input.Length).SequenceEqual(input));
        Assert.Equal((byte)'z', output[input.Length - 1]);
        Assert.Equal(Suffix, System.Text.Encoding.UTF8.GetString(output.AsSpan(input.Length)));
    }

    [Fact]
    public async Task Large_file_never_writes_a_buffer_at_the_loh_threshold()
    {
        const int inputLength = 250_000;
        await using var source = new GeneratedReadStream(inputLength);
        await using var destination = new RecordingWriteStream();
        using var memoryPool = new TrackingMemoryPool();
        var sut = new DateAndRandomSequenceMutator(
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 22, 23, 59, 58, TimeSpan.Zero)),
            new FixedRandomSequenceGenerator("0123456789ABCDEF"),
            memoryPool);

        await sut.MutateAsync(source, destination);

        Assert.Equal(inputLength + System.Text.Encoding.UTF8.GetByteCount(Suffix), destination.BytesWritten);
        Assert.InRange(destination.MaximumWriteSize, 1, 84_999);
        Assert.InRange(memoryPool.MaximumRentedBufferSize, 1, 84_999);
    }

    private static async Task<byte[]> MutateAsync(byte[] input)
    {
        await using var source = new MemoryStream(input, writable: false);
        await using var destination = new MemoryStream();

        await CreateMutator().MutateAsync(source, destination);

        return destination.ToArray();
    }

    private static DateAndRandomSequenceMutator CreateMutator() =>
        new(
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 22, 23, 59, 58, TimeSpan.Zero)),
            new FixedRandomSequenceGenerator("0123456789ABCDEF"));

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new BufferSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length,
            };
            Next = segment;
            return segment;
        }
    }

    private sealed class TrackingMemoryPool : MemoryPool<byte>
    {
        public int MaximumRentedBufferSize { get; private set; }
        public override int MaxBufferSize => MemoryPool<byte>.Shared.MaxBufferSize;

        public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
            var owner = MemoryPool<byte>.Shared.Rent(minBufferSize);
            MaximumRentedBufferSize = Math.Max(MaximumRentedBufferSize, owner.Memory.Length);
            return owner;
        }

        protected override void Dispose(bool disposing)
        {
        }
    }

    private sealed class GeneratedReadStream : Stream
    {
        private readonly long _length;
        private long _remaining;

        public GeneratedReadStream(long length)
        {
            _length = length;
            _remaining = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(buffer.Length, _remaining);
            buffer.Span[..count].Fill((byte)'x');
            _remaining -= count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RecordingWriteStream : Stream
    {
        public int MaximumWriteSize { get; private set; }
        public long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MaximumWriteSize = Math.Max(MaximumWriteSize, buffer.Length);
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

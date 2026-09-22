using System.Buffers;
using System.IO.Pipelines;
using FileMutation.Domain.Ports;
using FileMutation.Infrastructure;
using FileMutation.TestCommon;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileMutation.Infrastructure.Tests;

public sealed class FileMutatorAdapterTests
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
        var output = await MutateAsync(new SegmentedReadStream(
            "first"u8.ToArray(),
            "-second"u8.ToArray(),
            "-third"u8.ToArray()));

        Assert.Equal($"first-second-third{Suffix}", System.Text.Encoding.UTF8.GetString(output));
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
        var sut = CreateMutator(memoryPool);

        await sut.MutateAsync(source, destination);

        Assert.Equal(inputLength + System.Text.Encoding.UTF8.GetByteCount(Suffix), destination.BytesWritten);
        Assert.InRange(destination.MaximumWriteSize, 1, 84_999);
        Assert.InRange(memoryPool.MaximumRentedBufferSize, 1, 84_999);
    }

    private static async Task<byte[]> MutateAsync(byte[] input)
    {
        await using var source = new MemoryStream(input, writable: false);
        return await MutateAsync(source);
    }

    private static async Task<byte[]> MutateAsync(Stream source)
    {
        await using var destination = new MemoryStream();

        await CreateMutator().MutateAsync(source, destination);

        return destination.ToArray();
    }

    private static IFileMutator CreateMutator(MemoryPool<byte>? memoryPool = null)
    {
        var services = new ServiceCollection()
            .AddFileMutationInfrastructure()
            .AddSingleton<TimeProvider>(
                new FixedTimeProvider(new DateTimeOffset(2026, 9, 22, 23, 59, 58, TimeSpan.Zero)))
            .AddSingleton<IRandomSequenceGenerator>(new FixedRandomSequenceGenerator("0123456789ABCDEF"));
        if (memoryPool is not null)
        {
            services.AddSingleton<MemoryPool<byte>>(memoryPool);
        }

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IFileMutator>();
    }

    private sealed class SegmentedReadStream(params byte[][] parts) : Stream
    {
        private int _partIndex;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => parts.Sum(part => part.Length);
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_partIndex >= parts.Length || buffer.IsEmpty)
            {
                return ValueTask.FromResult(0);
            }

            var part = parts[_partIndex++];
            var count = Math.Min(buffer.Length, part.Length);
            part.AsSpan(0, count).CopyTo(buffer.Span);
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

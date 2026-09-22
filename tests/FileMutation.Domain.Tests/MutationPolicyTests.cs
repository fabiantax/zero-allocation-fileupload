using FileMutation.Domain;
using Xunit;

namespace FileMutation.Domain.Tests;

public sealed class MutationPolicyTests
{
    [Fact]
    public void Writes_stable_suffix_into_stack_allocated_span()
    {
        var context = new MutationContext(
            new DateTimeOffset(2026, 9, 23, 1, 0, 0, TimeSpan.FromHours(2)),
            "Ab9".AsSpan());
        Span<byte> destination = stackalloc byte[15];

        var written = MutationPolicy.TryWriteSuffix(destination, in context, out var bytesWritten);

        Assert.True(written);
        Assert.Equal(15, bytesWritten);
        Assert.True(destination.SequenceEqual("\n2026-09-22:Ab9"u8));
    }

    [Fact]
    public void Returns_false_without_partially_writing_when_destination_is_too_small()
    {
        var context = new MutationContext(DateTimeOffset.UnixEpoch, "abc".AsSpan());
        Span<byte> destination = stackalloc byte[14];
        Span<byte> expected = stackalloc byte[14];
        destination.Fill(42);
        expected.Fill(42);

        var written = MutationPolicy.TryWriteSuffix(destination, in context, out var bytesWritten);

        Assert.False(written);
        Assert.Equal(0, bytesWritten);
        Assert.True(destination.SequenceEqual(expected));
    }
}

using FileMutation.Domain.Batches;
using Xunit;

namespace FileMutation.Domain.Tests;

public sealed class BatchIdTests
{
    [Fact]
    public void New_produces_32_lowercase_hex_characters()
    {
        var value = BatchId.New().Value;

        Assert.Equal(32, value.Length);
        Assert.All(value, character => Assert.Contains(character, "0123456789abcdef"));
    }

    [Fact]
    public void New_produces_a_different_value_each_time() =>
        Assert.NotEqual(BatchId.New(), BatchId.New());

    [Fact]
    public void TryParse_round_trips_a_new_identifier()
    {
        var original = BatchId.New();

        Assert.True(BatchId.TryParse(original.Value, out var parsed));
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void ToString_returns_the_value()
    {
        var id = BatchId.New();

        Assert.Equal(id.Value, id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcdeF")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData("0123456789abcdef0123456789abcde ")]
    [InlineData("0123456789abcdef-123456789abcdef")]
    public void TryParse_rejects_anything_but_32_lowercase_hex_characters(string? value)
    {
        Assert.False(BatchId.TryParse(value, out var id));
        Assert.Equal(default, id);
    }

    [Theory]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("ffffffffffffffffffffffffffffffff")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public void TryParse_accepts_the_boundary_characters(string value)
    {
        Assert.True(BatchId.TryParse(value, out var id));
        Assert.Equal(value, id.Value);
    }
}

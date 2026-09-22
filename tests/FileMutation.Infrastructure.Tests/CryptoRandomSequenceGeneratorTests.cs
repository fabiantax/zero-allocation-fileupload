using Xunit;

namespace FileMutation.Infrastructure.Tests;

public sealed class CryptoRandomSequenceGeneratorTests
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    [Fact]
    public void Fill_populates_every_character_from_the_configured_alphabet()
    {
        var destination = new string('?', 64).ToCharArray();

        new CryptoRandomSequenceGenerator().Fill(destination);

        Assert.DoesNotContain('?', destination);
        Assert.All(destination, character => Assert.Contains(character, Alphabet));
    }

    [Fact]
    public void Two_calls_produce_different_sequences()
    {
        var generator = new CryptoRandomSequenceGenerator();
        var first = new char[64];
        var second = new char[64];

        generator.Fill(first);
        generator.Fill(second);

        Assert.NotEqual(new string(first), new string(second));
    }
}

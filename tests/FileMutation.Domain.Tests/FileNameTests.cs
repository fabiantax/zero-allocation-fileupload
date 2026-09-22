using FileMutation.Domain;
using Xunit;

namespace FileMutation.Domain.Tests;

public sealed class FileNameTests
{
    [Fact]
    public void Constructor_preserves_valid_name() =>
        Assert.Equal("notes.txt", new FileName("notes.txt").Value);

    [Theory]
    [InlineData("../notes.txt")]
    [InlineData("..\\notes.txt")]
    [InlineData("folder/notes.txt")]
    public void Constructor_rejects_path_syntax(string value) =>
        Assert.Throws<ArgumentException>(() => new FileName(value));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_empty_name(string value) =>
        Assert.Throws<ArgumentException>(() => new FileName(value));

    [Fact]
    public void Constructor_preserves_non_ascii_name() =>
        Assert.Equal("résumé-日本語.txt", new FileName("résumé-日本語.txt").Value);

    [Fact]
    public void Constructor_trims_name_for_safe_download() =>
        Assert.Equal("notes.txt", new FileName("  notes.txt  ").Value);
}

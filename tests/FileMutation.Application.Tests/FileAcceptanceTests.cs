using FileMutation.Application;
using Xunit;

namespace FileMutation.Application.Tests;

public sealed class FileAcceptanceTests
{
    [Theory]
    [InlineData(new byte[] { 72, 101, 108, 108, 111 })]
    [InlineData(new byte[] { 239, 187, 191, 72, 101, 108, 108, 111 })]
    [InlineData(new byte[] { 240, 159, 140, 141 })]
    public void Accepts_txt_plain_text_with_valid_utf8(byte[] content)
    {
        var result = FileAcceptance.Evaluate(content, "résumé.txt", "text/plain");

        Assert.True(result.IsAccepted);
        Assert.Equal("résumé.txt", result.FileName!.Value);
        Assert.Null(result.RejectionReason);
    }

    [Theory]
    [InlineData("data.json")]
    [InlineData("data.csv")]
    [InlineData("data")]
    public void Rejects_unsupported_extension(string fileName)
    {
        var result = FileAcceptance.Evaluate("valid"u8, fileName, "text/plain");

        Assert.False(result.IsAccepted);
        Assert.Equal(FileRejectionReason.UnsupportedFileExtension, result.RejectionReason);
    }

    [Fact]
    public void Rejects_unsupported_content_type()
    {
        var result = FileAcceptance.Evaluate("valid"u8, "data.txt", "application/octet-stream");

        Assert.Equal(FileRejectionReason.UnsupportedContentType, result.RejectionReason);
    }

    [Fact]
    public void Rejects_invalid_utf8_without_throwing()
    {
        byte[] invalidUtf8 = [0xC3, 0x28];

        var result = FileAcceptance.Evaluate(invalidUtf8, "data.txt", "text/plain");

        Assert.Equal(FileRejectionReason.InvalidUtf8, result.RejectionReason);
    }

    [Fact]
    public void Rejects_invalid_file_name_without_throwing()
    {
        var result = FileAcceptance.Evaluate("valid"u8, "../data.txt", "text/plain");

        Assert.Equal(FileRejectionReason.InvalidFileName, result.RejectionReason);
    }
}

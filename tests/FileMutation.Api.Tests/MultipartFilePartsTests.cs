using System.Text;
using FileMutation.Api.Multipart;
using Xunit;

namespace FileMutation.Api.Tests;

/// <summary>
/// Tests for the multipart file-part reader. <see cref="MultipartFileParts"/> is public so these
/// tests call it directly, with no reflection and no <c>InternalsVisibleTo</c>.
/// </summary>
public sealed class MultipartFilePartsTests
{
    private const string Boundary = "test-boundary";

    private sealed record Part(string FileName, string? ContentType, string Body);

    [Theory]
    [InlineData("multipart/form-data; boundary=abc", true, "abc")]
    [InlineData("multipart/form-data; boundary=\"abc\"", true, "abc")]
    [InlineData("MULTIPART/FORM-DATA; boundary=abc", true, "abc")]
    [InlineData("multipart/form-data", false, "")]
    [InlineData("multipart/form-data; boundary=\"  \"", false, "")]
    [InlineData("text/plain; boundary=abc", false, "")]
    [InlineData("", false, "")]
    [InlineData(null, false, "")]
    public void TryGetBoundary_accepts_only_form_data_with_a_boundary(string? contentType, bool expected, string boundary)
    {
        var result = MultipartFileParts.TryGetBoundary(contentType, out var actualBoundary);

        Assert.Equal(expected, result);
        if (expected)
        {
            Assert.Equal(boundary, actualBoundary);
        }
    }

    [Fact]
    public async Task ReadAsync_prefers_filename_star_over_filename()
    {
        var body = Multipart(("file", "filename=\"plain.txt\"; filename*=UTF-8''star.txt", "text/plain", "x"));

        var part = Assert.Single(await ReadAllAsync(body, "file"));

        Assert.Equal("star.txt", part.FileName);
    }

    [Fact]
    public async Task ReadAsync_removes_quotes_from_the_file_name()
    {
        var body = Multipart(("file", "filename=\"quoted name.txt\"", "text/plain", "x"));

        var part = Assert.Single(await ReadAllAsync(body, "file"));

        Assert.Equal("quoted name.txt", part.FileName);
    }

    [Fact]
    public async Task ReadAsync_skips_parts_without_a_file_name()
    {
        var body = Multipart(("file", null, null, "just a field"), ("file", "filename=\"a.txt\"", "text/plain", "kept"));

        var part = Assert.Single(await ReadAllAsync(body, "file"));

        Assert.Equal("a.txt", part.FileName);
        Assert.Equal("kept", part.Body);
    }

    [Fact]
    public async Task ReadAsync_skips_parts_with_another_field_name()
    {
        var body = Multipart(("other", "filename=\"a.txt\"", "text/plain", "no"), ("file", "filename=\"b.txt\"", "text/plain", "yes"));

        var part = Assert.Single(await ReadAllAsync(body, "file"));

        Assert.Equal("b.txt", part.FileName);
    }

    [Fact]
    public async Task ReadAsync_yields_parts_in_order_with_full_bodies_and_content_types()
    {
        var first = new string('a', 20_000);
        var second = new string('b', 20_000);
        var body = Multipart(("file", "filename=\"1.txt\"", "text/plain", first), ("file", "filename=\"2.txt\"", null, second));

        var parts = await ReadAllAsync(body, "file");

        Assert.Equal(
            [new Part("1.txt", "text/plain", first), new Part("2.txt", null, second)],
            parts);
    }

    [Fact]
    public async Task ReadAsync_propagates_InvalidDataException_for_a_malformed_body()
    {
        var malformed = new MemoryStream(Encoding.UTF8.GetBytes("this is not multipart at all"));

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => ReadAllAsync(malformed, "file"));

        Assert.True(thrown is InvalidDataException or IOException, thrown.GetType().FullName);
    }

    private static MemoryStream Multipart(params (string Name, string? FileDisposition, string? ContentType, string Content)[] sections)
    {
        var builder = new StringBuilder();
        foreach (var (name, fileDisposition, contentType, content) in sections)
        {
            builder.Append("--").Append(Boundary).Append("\r\n");
            builder.Append("Content-Disposition: form-data; name=\"").Append(name).Append('"');
            if (fileDisposition is not null)
            {
                builder.Append("; ").Append(fileDisposition);
            }

            builder.Append("\r\n");
            if (contentType is not null)
            {
                builder.Append("Content-Type: ").Append(contentType).Append("\r\n");
            }

            builder.Append("\r\n").Append(content).Append("\r\n");
        }

        builder.Append("--").Append(Boundary).Append("--\r\n");
        return new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static async Task<List<Part>> ReadAllAsync(Stream body, string fieldName)
    {
        var parts = new List<Part>();
        await foreach (var part in MultipartFileParts.ReadAsync(body, Boundary, fieldName, CancellationToken.None))
        {
            using var reader = new StreamReader(part.Body, Encoding.UTF8);
            parts.Add(new Part(part.FileName, part.ContentType, await reader.ReadToEndAsync()));
        }

        return parts;
    }
}

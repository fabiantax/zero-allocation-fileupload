using System.Buffers;
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

    [Fact]
    public void Accepts_case_insensitive_metadata_with_content_type_whitespace()
    {
        var result = FileAcceptance.Evaluate("valid"u8, "DATA.TXT", "  TEXT/PLAIN  ");

        Assert.True(result.IsAccepted);
        Assert.Equal("DATA.TXT", result.FileName!.Value);
    }

    [Fact]
    public void Accepts_multibyte_utf8_character_split_across_segments()
    {
        var content = CreateSequence(
            new byte[] { (byte)'a', 0xF0, 0x9F },
            new byte[] { 0x8C, 0x8D, (byte)'z' });

        Assert.False(content.IsSingleSegment);

        var result = FileAcceptance.Evaluate(in content, "data.txt", "text/plain");

        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void Rejects_utf8_that_becomes_invalid_at_the_end_of_the_last_segment()
    {
        var content = CreateSequence(
            new byte[] { (byte)'a', 0xE0 },
            new byte[] { 0xA0 },
            new byte[] { 0x41 });

        Assert.False(content.IsSingleSegment);

        var result = FileAcceptance.Evaluate(in content, "data.txt", "text/plain");

        Assert.Equal(FileRejectionReason.InvalidUtf8, result.RejectionReason);
    }

    [Fact]
    public void Rejects_truncated_multibyte_utf8_sequence_at_the_final_segment()
    {
        var content = CreateSequence(
            new byte[] { (byte)'a', 0xF0, 0x9F },
            new byte[] { 0x8C });

        Assert.False(content.IsSingleSegment);

        var result = FileAcceptance.Evaluate(in content, "data.txt", "text/plain");

        Assert.Equal(FileRejectionReason.InvalidUtf8, result.RejectionReason);
    }

    [Fact]
    public void Single_and_multi_segment_inputs_with_identical_bytes_have_the_same_result()
    {
        byte[] bytes = [(byte)'a', 0xC3, 0x28];
        var content = CreateSequence(
            new byte[] { (byte)'a', 0xC3 },
            new byte[] { 0x28 });

        Assert.False(content.IsSingleSegment);

        var singleSegmentResult = FileAcceptance.Evaluate(bytes, "data.txt", "text/plain");
        var multiSegmentResult = FileAcceptance.Evaluate(in content, "data.txt", "text/plain");

        Assert.Equal(singleSegmentResult.IsAccepted, multiSegmentResult.IsAccepted);
        Assert.Equal(singleSegmentResult.RejectionReason, multiSegmentResult.RejectionReason);
        Assert.Equal(singleSegmentResult.FileName?.Value, multiSegmentResult.FileName?.Value);
    }

    private static ReadOnlySequence<byte> CreateSequence(params ReadOnlyMemory<byte>[] buffers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(buffers.Length, 2);

        var first = new BufferSegment(buffers[0]);
        var last = first;
        foreach (var buffer in buffers[1..])
        {
            last = last.Append(buffer);
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public BufferSegment Append(ReadOnlyMemory<byte> next)
        {
            var segment = new BufferSegment(next) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }
}

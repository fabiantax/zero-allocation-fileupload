using System.Buffers;
using System.Text;
using FileMutation.Domain;

namespace FileMutation.Application;

/// <summary>
/// Applies the single HTTP-independent acceptance rule for uploaded files.
/// See <see href="../../docs/adr/0009-accepted-formats-and-mutator-dispatch.md">ADR 0009</see>.
/// </summary>
public static class FileAcceptance
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private const int DecodeBufferChars = 8 * 1024;

    /// <summary>Decides whether an upload is acceptable, from its bytes and declared metadata alone.</summary>
    public static FileAcceptanceResult Evaluate(
        ReadOnlySpan<byte> content,
        string? declaredFileName,
        string? declaredContentType)
    {
        var metadata = EvaluateMetadata(declaredFileName, declaredContentType);
        return !metadata.IsAccepted || IsUtf8(content)
            ? metadata
            : FileAcceptanceResult.Rejected(FileRejectionReason.InvalidUtf8);
    }

    /// <summary>
    /// The same decision over a segmented buffer, so a pooled <see cref="System.IO.Pipelines.Pipe"/>
    /// never has to be flattened into one array to be validated.
    /// </summary>
    public static FileAcceptanceResult Evaluate(
        in ReadOnlySequence<byte> content,
        string? declaredFileName,
        string? declaredContentType)
    {
        var metadata = EvaluateMetadata(declaredFileName, declaredContentType);
        return !metadata.IsAccepted || IsUtf8(in content)
            ? metadata
            : FileAcceptanceResult.Rejected(FileRejectionReason.InvalidUtf8);
    }

    private static FileAcceptanceResult EvaluateMetadata(string? declaredFileName, string? declaredContentType)
    {
        if (!FileName.TryCreate(declaredFileName, out var fileName))
        {
            return FileAcceptanceResult.Rejected(FileRejectionReason.InvalidFileName);
        }

        if (!fileName.Value.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return FileAcceptanceResult.Rejected(FileRejectionReason.UnsupportedFileExtension);
        }

        if (!string.Equals(declaredContentType?.Trim(), "text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return FileAcceptanceResult.Rejected(FileRejectionReason.UnsupportedContentType);
        }

        return FileAcceptanceResult.Accepted(fileName);
    }

    private static bool IsUtf8(ReadOnlySpan<byte> content)
    {
        try
        {
            _ = StrictUtf8.GetCharCount(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsUtf8(in ReadOnlySequence<byte> content)
    {
        if (content.IsSingleSegment)
        {
            return IsUtf8(content.FirstSpan);
        }

        var decoder = StrictUtf8.GetDecoder();
        char[] characters = ArrayPool<char>.Shared.Rent(DecodeBufferChars);
        try
        {
            var position = content.Start;
            while (content.TryGet(ref position, out var segment))
            {
                // A multi-byte character may straddle a segment boundary, so the decoder carries
                // state across segments and only flushes once, after the last one.
                var isLastSegment = position.GetObject() is null;
                var remaining = segment.Span;
                do
                {
                    decoder.Convert(remaining, characters, isLastSegment, out var bytesUsed, out _, out _);
                    remaining = remaining[bytesUsed..];
                }
                while (!remaining.IsEmpty);
            }

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(characters);
        }
    }
}

/// <summary>Identifies why an uploaded file failed the acceptance rule.</summary>
public enum FileRejectionReason
{
    /// <summary>The submitted filename is missing or unsafe.</summary>
    InvalidFileName,

    /// <summary>The filename does not have the supported <c>.txt</c> extension.</summary>
    UnsupportedFileExtension,

    /// <summary>The declared multipart content type is not <c>text/plain</c>.</summary>
    UnsupportedContentType,

    /// <summary>The complete file content is not valid UTF-8.</summary>
    InvalidUtf8
}

/// <summary>Describes either an accepted safe filename or one rejection reason.</summary>
public sealed class FileAcceptanceResult
{
    private FileAcceptanceResult(FileName? fileName, FileRejectionReason? rejectionReason)
    {
        FileName = fileName;
        RejectionReason = rejectionReason;
    }

    /// <summary>Gets whether the upload satisfies every acceptance rule.</summary>
    public bool IsAccepted => FileName is not null;

    /// <summary>Gets the safe download filename when the upload is accepted.</summary>
    public FileName? FileName { get; }

    /// <summary>Gets the rejection reason when the upload is not accepted.</summary>
    public FileRejectionReason? RejectionReason { get; }

    internal static FileAcceptanceResult Accepted(FileName fileName) => new(fileName, null);

    internal static FileAcceptanceResult Rejected(FileRejectionReason reason) => new(null, reason);
}

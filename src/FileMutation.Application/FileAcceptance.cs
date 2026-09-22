using System.Text;
using FileMutation.Domain;

namespace FileMutation.Application;

public static class FileAcceptance
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static FileAcceptanceResult Evaluate(
        ReadOnlySpan<byte> content,
        string? declaredFileName,
        string? declaredContentType)
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

        try
        {
            _ = StrictUtf8.GetCharCount(content);
        }
        catch (DecoderFallbackException)
        {
            return FileAcceptanceResult.Rejected(FileRejectionReason.InvalidUtf8);
        }

        return FileAcceptanceResult.Accepted(fileName);
    }
}

public enum FileRejectionReason
{
    InvalidFileName,
    UnsupportedFileExtension,
    UnsupportedContentType,
    InvalidUtf8
}

public sealed class FileAcceptanceResult
{
    private FileAcceptanceResult(FileName? fileName, FileRejectionReason? rejectionReason)
    {
        FileName = fileName;
        RejectionReason = rejectionReason;
    }

    public bool IsAccepted => FileName is not null;

    public FileName? FileName { get; }

    public FileRejectionReason? RejectionReason { get; }

    internal static FileAcceptanceResult Accepted(FileName fileName) => new(fileName, null);

    internal static FileAcceptanceResult Rejected(FileRejectionReason reason) => new(null, reason);
}

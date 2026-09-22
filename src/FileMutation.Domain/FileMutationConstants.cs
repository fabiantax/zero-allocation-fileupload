namespace FileMutation.Domain;

/// <summary>Shared sizing constants for the file mutation pipeline.</summary>
public static class FileMutationConstants
{
    /// <summary>The pooled pipe segment size used for buffered and streamed file content.</summary>
    public const int SegmentSize = 16 * 1024;
}

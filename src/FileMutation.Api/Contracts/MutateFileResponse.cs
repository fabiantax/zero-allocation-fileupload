using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace FileMutation.Api.Contracts;

/// <summary>Describes the file download returned after mutation.</summary>
public sealed class MutateFileResponse
{
    /// <summary>The streaming content of the mutated file.</summary>
    [Required]
    [Description("The mutated file content.")]
    public required Stream Content { get; init; }

    /// <summary>The media type of the download.</summary>
    [Required]
    [Description("The response media type; text/plain for the supported format.")]
    public required string ContentType { get; init; }

    /// <summary>The sanitised original filename used for the download.</summary>
    [Required]
    [Description("The sanitised original filename.")]
    public required string DownloadName { get; init; }
}

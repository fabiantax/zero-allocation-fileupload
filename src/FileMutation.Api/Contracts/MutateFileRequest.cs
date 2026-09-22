using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace FileMutation.Api.Contracts;

/// <summary>
/// Multipart request containing the text file to mutate.
/// See <see href="../../../docs/adr/0009-accepted-formats-and-mutator-dispatch.md">ADR 0009</see>.
/// </summary>
public sealed class MutateFileRequest
{
    /// <summary>The multipart form field that carries the file to mutate.</summary>
    public const string FileFieldName = "file";

    /// <summary>A UTF-8 .txt file declared as text/plain.</summary>
    [Required]
    [Description("A UTF-8 .txt file declared as text/plain.")]
    [FromForm(Name = FileFieldName)]
    public required IFormFile File { get; init; }
}

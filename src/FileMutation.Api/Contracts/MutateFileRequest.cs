using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace FileMutation.Api.Contracts;

/// <summary>
/// Multipart request containing the text file to mutate.
/// See <see href="../../../docs/adr/0009-accepted-formats-and-mutator-dispatch.md">ADR 0009</see>.
/// </summary>
public sealed class MutateFileRequest
{
    /// <summary>The UTF-8 <c>.txt</c> file to mutate.</summary>
    [Required]
    [Description("A UTF-8 .txt file declared as text/plain.")]
    public required IFormFile File { get; init; }
}

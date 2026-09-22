using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace FileMutation.Api.Contracts;

/// <summary>Multipart request containing the text file to mutate.</summary>
public sealed class MutateFileRequest
{
    /// <summary>The UTF-8 <c>.txt</c> file to mutate.</summary>
    [Required]
    [Description("A UTF-8 .txt file declared as text/plain.")]
    public required IFormFile File { get; init; }
}

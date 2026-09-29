using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace FileMutation.Api.Multipart;

/// <summary>One file part of a multipart request: its body stream and the metadata its headers declared.</summary>
/// <param name="Body">The part body. Valid only until the next part is requested from the reader.</param>
/// <param name="FileName">The declared file name, with <c>filename*</c> preferred and quotes removed.</param>
/// <param name="ContentType">The part's declared content type, if any.</param>
internal readonly record struct MultipartFilePart(Stream Body, string FileName, string? ContentType);

/// <summary>
/// Boundary parsing and file-part filtering for <c>multipart/form-data</c> requests, shared by
/// every endpoint that accepts uploads so the parsing rules exist once.
/// </summary>
internal static class MultipartFileParts
{
    /// <summary>Reads the multipart boundary from a <c>Content-Type</c> header value.</summary>
    /// <param name="contentType">The raw header value.</param>
    /// <param name="boundary">The unquoted boundary when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> for <c>multipart/form-data</c> with a non-blank boundary.</returns>
    internal static bool TryGetBoundary(string? contentType, out string boundary)
    {
        boundary = string.Empty;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ||
            !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value ?? string.Empty;
        return !string.IsNullOrWhiteSpace(boundary);
    }

    /// <summary>
    /// Yields each <c>form-data</c> part named <paramref name="fieldName"/> that carries a file
    /// name, and skips the rest. The caller must finish with a part's body before advancing.
    /// <see cref="InvalidDataException"/> and <see cref="IOException"/> propagate unchanged.
    /// </summary>
    /// <param name="body">The request body.</param>
    /// <param name="boundary">The boundary from <see cref="TryGetBoundary"/>.</param>
    /// <param name="fieldName">The form field name file parts must use.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The matching file parts, in request order.</returns>
    internal static async IAsyncEnumerable<MultipartFilePart> ReadAsync(
        Stream body,
        string boundary,
        string fieldName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var multipart = new MultipartReader(boundary, body);
        while (await multipart.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (TryGetFileMetadata(section, fieldName, out var fileName, out var contentType))
            {
                yield return new MultipartFilePart(section.Body, fileName, contentType);
            }
        }
    }

    private static bool TryGetFileMetadata(
        MultipartSection section, string fieldName, out string fileName, out string? contentType)
    {
        fileName = string.Empty;
        contentType = null;

        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
            !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(disposition.Name.Value, fieldName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(disposition.FileNameStar.Value ?? disposition.FileName.Value))
        {
            return false;
        }

        fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.Value ?? disposition.FileName.Value).Value
            ?? string.Empty;
        contentType = section.ContentType;
        return true;
    }
}

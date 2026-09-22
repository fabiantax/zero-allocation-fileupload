using FileMutation.Application;
using FileMutation.Api.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FileMutation.Api.Endpoints;

/// <summary>
/// Configured ceilings for one upload: the file itself, plus multipart framing overhead.
/// Settable properties with a parameterless constructor because <c>IOptions</c> binds that way.
/// </summary>
internal sealed class UploadLimits
{
    /// <summary>The largest accepted file, in bytes.</summary>
    public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Headroom for multipart boundaries and headers on top of the file itself.</summary>
    public long MaxRequestOverheadBytes { get; set; } = 64 * 1024;
}

/// <summary>
/// Accepts a text file, appends the UTC date and a random sequence, and returns it under its
/// original name. See
/// <see href="../../../docs/adr/0005-streaming-allocation-strategy.md">ADR 0005</see> for the
/// two-phase ordering this endpoint exists to guarantee.
/// </summary>
internal static class FileMutateEndpoint
{
    /// <summary>Maps <c>POST /files/mutate</c> and its OpenAPI response contract.</summary>
    internal static RouteHandlerBuilder MapFileMutateEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/files/mutate", ExecuteAsync)
            .WithName("MutateFile")
            .Accepts<MutateFileRequest>("multipart/form-data")
            .Produces(StatusCodes.Status200OK, contentType: "text/plain")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
    }

    private static async Task<IResult> ExecuteAsync(
        HttpContext httpContext,
        SingleFileMutationService fileMutationService,
        IOptions<UploadLimits> uploadLimits,
        CancellationToken cancellationToken)
    {
        var limits = uploadLimits.Value;
        var request = httpContext.Request;
        if (request.ContentLength > limits.MaxFileBytes + limits.MaxRequestOverheadBytes)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "The upload exceeds the configured size limit.");
        }

        if (!TryGetBoundary(request.ContentType, out var boundary))
        {
            return Problem(StatusCodes.Status400BadRequest, "The request must be multipart/form-data.");
        }

        FileMutationResult? mutationResult = null;
        try
        {
            try
            {
                var multipart = new MultipartReader(boundary, request.Body);
                var fileParts = 0;
                while (await multipart.ReadNextSectionAsync(cancellationToken) is { } section)
                {
                    if (!TryGetFileMetadata(section, out var fileName, out var contentType))
                    {
                        continue; // Non-file parts are ignored rather than rejected.
                    }

                    if (++fileParts > 1)
                    {
                        return Problem(
                            StatusCodes.Status400BadRequest,
                            $"Supply exactly one file part named '{MutateFileRequest.FileFieldName}'.");
                    }
                    await DisposeResultAsync(mutationResult);

                    mutationResult = await fileMutationService.MutateAsync(
                        section.Body, fileName, contentType, limits.MaxFileBytes, cancellationToken);
                }
            }
            catch (InvalidDataException)
            {
                return Problem(StatusCodes.Status400BadRequest, "The multipart request is malformed.");
            }
            catch (IOException)
            {
                return Problem(StatusCodes.Status400BadRequest, "The multipart request is malformed.");
            }

            if (mutationResult is null)
            {
                return Problem(
                    StatusCodes.Status400BadRequest,
                    $"Supply exactly one non-empty file part named '{MutateFileRequest.FileFieldName}'.");
            }

            if (!mutationResult.IsAccepted)
            {
                return ProblemFor(mutationResult);
            }

            // PHASE 2 — validation and mutation completed, so the status line can now be committed.
            var response = httpContext.Response;
            response.ContentType = "text/plain";
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(mutationResult.FileName!.Value);
            response.Headers.ContentDisposition = disposition.ToString();
            await mutationResult.Content.CopyToAsync(response.Body, cancellationToken);
            return Results.Empty;
        }
        finally
        {
            await DisposeResultAsync(mutationResult);
        }
    }

    private static IResult ProblemFor(FileMutationResult result)
    {
        if (result.FailureReason == FileMutationFailureReason.MutationFailed)
        {
            throw new InvalidOperationException("The configured file mutator failed.");
        }

        return Problem(StatusFor(result), DetailFor(result));
    }

    private static ValueTask DisposeResultAsync(FileMutationResult? result) =>
        result?.DisposeAsync() ?? ValueTask.CompletedTask;

    private static int StatusFor(FileMutationResult result) => result switch
    {
        { FailureReason: FileMutationFailureReason.TooLarge } => StatusCodes.Status413PayloadTooLarge,
        { FailureReason: FileMutationFailureReason.Rejected, AcceptanceReason: FileRejectionReason.InvalidFileName }
            => StatusCodes.Status400BadRequest,
        { FailureReason: FileMutationFailureReason.Empty or FileMutationFailureReason.Unreadable }
            => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status415UnsupportedMediaType
    };

    private static string DetailFor(FileMutationResult result) => result switch
    {
        { FailureReason: FileMutationFailureReason.TooLarge } => "The upload exceeds the configured size limit.",
        { FailureReason: FileMutationFailureReason.Empty } =>
            $"Supply exactly one non-empty file part named '{MutateFileRequest.FileFieldName}'.",
        { FailureReason: FileMutationFailureReason.Unreadable } => "The multipart request is malformed.",
        { FailureReason: FileMutationFailureReason.Rejected, AcceptanceReason: FileRejectionReason.InvalidFileName }
            => "The file name is missing or not a valid file name.",
        { FailureReason: FileMutationFailureReason.Rejected, AcceptanceReason: FileRejectionReason.UnsupportedFileExtension }
            => "Only .txt files are accepted.",
        { FailureReason: FileMutationFailureReason.Rejected, AcceptanceReason: FileRejectionReason.UnsupportedContentType }
            => "Only text/plain content is accepted.",
        _ => "The uploaded file is not valid UTF-8 text."
    };

    private static bool TryGetBoundary(string? contentType, out string boundary)
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

    private static bool TryGetFileMetadata(MultipartSection section, out string? fileName, out string? contentType)
    {
        fileName = null;
        contentType = null;

        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
            !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(disposition.Name.Value, MutateFileRequest.FileFieldName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(disposition.FileNameStar.Value ?? disposition.FileName.Value))
        {
            return false;
        }

        fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.Value ?? disposition.FileName.Value).Value;
        contentType = section.ContentType;
        return true;
    }

    private static IResult Problem(int statusCode, string detail) =>
        TypedResults.Problem(statusCode: statusCode, detail: detail);
}

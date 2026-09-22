using System.Buffers;
using System.IO.Pipelines;
using FileMutation.Application;
using FileMutation.Domain.Ports;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FileMutation.Api.Endpoints;

/// <summary>
/// Configured ceilings for one upload: the file itself, plus multipart framing overhead.
/// Settable properties with a parameterless constructor because <c>IOptions</c> binds that way.
/// </summary>
public sealed class UploadLimits
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
public static class FileMutateEndpoint
{
    private const int SegmentSize = 16 * 1024;
    private const string FileFieldName = "file";

    /// <summary>Maps <c>POST /files/mutate</c> and its OpenAPI response contract.</summary>
    public static RouteHandlerBuilder MapFileMutateEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/files/mutate", ExecuteAsync)
            .WithName("MutateFile")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces(StatusCodes.Status200OK, contentType: "text/plain")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
    }

    private static async Task<IResult> ExecuteAsync(
        HttpContext httpContext,
        IFileMutator fileMutator,
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

        // PHASE 1 — read the whole upload into pooled segments. Nothing is written to the response
        // yet, so every rejection below can still choose its own status code.
        var upload = new Pipe(new PipeOptions(
            minimumSegmentSize: SegmentSize,
            pauseWriterThreshold: limits.MaxFileBytes + 1,
            resumeWriterThreshold: limits.MaxFileBytes));

        string? fileName = null;
        string? contentType = null;
        var fileParts = 0;
        long fileBytes = 0;

        try
        {
            // No BodyLengthLimit: the size rule is enforced below by counting, so there is exactly
            // one place that decides 413 and no exception message to pattern-match.
            var multipart = new MultipartReader(boundary, request.Body);

            while (await multipart.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                if (!TryGetFileMetadata(section, out var sectionFileName, out var sectionContentType))
                {
                    continue; // ponytail: non-file parts are ignored rather than rejected.
                }

                if (++fileParts > 1)
                {
                    return Problem(StatusCodes.Status400BadRequest, $"Supply exactly one file part named '{FileFieldName}'.");
                }

                fileName = sectionFileName;
                contentType = sectionContentType;

                var sectionReader = PipeReader.Create(
                    section.Body,
                    new StreamPipeReaderOptions(bufferSize: SegmentSize, leaveOpen: true));
                try
                {
                    while (true)
                    {
                        var read = await sectionReader.ReadAsync(cancellationToken);
                        fileBytes += read.Buffer.Length;

                        // Counted here rather than trusted to MultipartReader.BodyLengthLimit: the
                        // limit is the rule, so the rule is enforced where it can be seen.
                        if (fileBytes > limits.MaxFileBytes)
                        {
                            sectionReader.AdvanceTo(read.Buffer.Start, read.Buffer.End);
                            return Problem(StatusCodes.Status413PayloadTooLarge, "The upload exceeds the configured size limit.");
                        }

                        foreach (var segment in read.Buffer)
                        {
                            upload.Writer.Write(segment.Span);
                        }

                        sectionReader.AdvanceTo(read.Buffer.End);
                        if (read.IsCompleted)
                        {
                            break;
                        }
                    }

                    await upload.Writer.FlushAsync(cancellationToken);
                }
                finally
                {
                    await sectionReader.CompleteAsync();
                }
            }
        }
        // MultipartReader signals a malformed body as InvalidDataException, and a truncated or
        // empty one as IOException ("Unexpected end of Stream"). Both are the client's mistake.
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return Problem(StatusCodes.Status400BadRequest, "The multipart request is malformed.");
        }
        finally
        {
            await upload.Writer.CompleteAsync();
        }

        var buffered = await upload.Reader.ReadAsync(cancellationToken);
        var content = buffered.Buffer;

        if (fileParts == 0 || content.IsEmpty)
        {
            await upload.Reader.CompleteAsync();
            return Problem(StatusCodes.Status400BadRequest, $"Supply exactly one non-empty file part named '{FileFieldName}'.");
        }

        // The acceptance decision is an Application rule over bytes and declared metadata. It knows
        // no HTTP; the endpoint only maps its result onto a status code.
        var acceptance = FileAcceptance.Evaluate(in content, fileName, contentType);
        if (!acceptance.IsAccepted)
        {
            await upload.Reader.CompleteAsync();
            var reason = acceptance.RejectionReason!.Value;
            return Problem(StatusFor(reason), DetailFor(reason));
        }

        // PHASE 2 — the status line is committed from here. Nothing below may reject.
        upload.Reader.AdvanceTo(content.Start, content.End);

        var response = httpContext.Response;
        response.ContentType = "text/plain";
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(acceptance.FileName!.Value);
        response.Headers.ContentDisposition = disposition.ToString();

        try
        {
            await fileMutator.MutateAsync(
                upload.Reader.AsStream(leaveOpen: true),
                response.BodyWriter.AsStream(leaveOpen: true),
                cancellationToken);
        }
        finally
        {
            await upload.Reader.CompleteAsync();
        }

        return Results.Empty;
    }

    private static int StatusFor(FileRejectionReason reason) => reason switch
    {
        FileRejectionReason.InvalidFileName => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status415UnsupportedMediaType
    };

    private static string DetailFor(FileRejectionReason reason) => reason switch
    {
        FileRejectionReason.InvalidFileName => "The file name is missing or not a valid file name.",
        FileRejectionReason.UnsupportedFileExtension => "Only .txt files are accepted.",
        FileRejectionReason.UnsupportedContentType => "Only text/plain content is accepted.",
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
            !string.Equals(disposition.Name.Value, FileFieldName, StringComparison.Ordinal) ||
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

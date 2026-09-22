using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FileMutation.Api.ExceptionHandling;

/// <summary>
/// Logs unexpected exceptions and writes a generic problem response when the response has not
/// started.
/// </summary>
/// <param name="logger">The application logger.</param>
/// <param name="problemDetailsService">The framework problem-details writer.</param>
public sealed class ProblemDetailsExceptionHandler(
    ILogger<ProblemDetailsExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    /// <summary>Attempts to translate an unexpected exception into an RFC 7807 response.</summary>
    /// <param name="httpContext">The failing request context.</param>
    /// <param name="exception">The unhandled exception.</param>
    /// <param name="cancellationToken">Signals that response writing should stop.</param>
    /// <returns><see langword="true"/> when a problem response was written.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled request failure. TraceId: {TraceId}",
            Activity.Current?.Id ?? httpContext.TraceIdentifier);

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "The request could not be completed."
        };
        problemDetails.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
        return true;
    }
}

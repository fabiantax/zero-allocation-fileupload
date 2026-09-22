using System.Text.Json.Serialization;
using FileMutation.Api;
using FileMutation.Api.Endpoints;
using FileMutation.Api.ExceptionHandling;
using FileMutation.Application;
using FileMutation.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;

// --- Host -----------------------------------------------------------------

// Slim hosting does not configure HTTPS automatically. Configure it explicitly so an HTTPS
// address starts successfully; TestServer cannot detect this because it never binds a socket.
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseKestrelHttpsConfiguration();

// Fail at startup on DI misconfiguration (missing registrations, captive scopes).
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

// --- Configuration ----------------------------------------------------------

var uploadSection = builder.Configuration.GetSection("Upload");
var uploadLimits = new UploadLimits();
uploadSection.Bind(uploadLimits);

builder.Services.AddOptions<UploadLimits>()
    .Bind(uploadSection)
    .Validate(
        limits => limits.MaxFileBytes > 0 && limits.MaxRequestOverheadBytes >= 0,
        "MaxFileBytes must be greater than zero and MaxRequestOverheadBytes must be non-negative.")
    .ValidateOnStart();

// Outer guard: refuse a request before reading it. The precise per-file rule lives in the endpoint.
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize =
        checked(uploadLimits.MaxFileBytes + uploadLimits.MaxRequestOverheadBytes));

// --- Error handling ---------------------------------------------------------

// RFC 7807 bodies for unexpected exceptions; rejections are results, not exceptions.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

// --- Application services ---------------------------------------------------

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddFileMutationInfrastructure();

// Scoped, not singleton: it is stateless today, but scoped survives the next per-request
// dependency added (a DbContext would be captive in a singleton). See .claude/rules/dotnet-conventions.md.
builder.Services.AddScoped<SingleFileMutationService>();

// --- OpenAPI + JSON ---------------------------------------------------------

// Source-generated serialization for ProblemDetails; reflection JSON is AOT-hostile.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonSerializerContext.Default));
builder.Services.AddOpenApi();

// --- Minimal API pipeline ---------------------------------------------------

var app = builder.Build();

app.MapOpenApi();                 // /openapi/v1.json — the machine contract
app.MapScalarApiReference();      // /scalar — the browser UI
app.UseExceptionHandler();        // last-chance handler for unexpected faults
app.MapFileMutateEndpoint();      // POST /files/mutate

await app.RunAsync();

namespace FileMutation.Api
{
    [JsonSerializable(typeof(ProblemDetails))]
    internal partial class ApiJsonSerializerContext : JsonSerializerContext;
}

/// <summary>
/// Provides the application entry point used by the host and integration-test factory.
/// See <see href="../../docs/decision-log.md#an-openapi-ui-without-swashbuckle">the OpenAPI decision</see>.
/// </summary>
public partial class Program
{
    /// <summary>Supports type references from the integration-test host; never instantiated directly.</summary>
    protected Program()
    {
    }
}

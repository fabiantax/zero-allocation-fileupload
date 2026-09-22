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

// CreateSlimBuilder omits HTTPS wiring; without this call any https:// address throws at
// startup — a failure no WebApplicationFactory test can catch (TestServer binds no socket).
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseKestrelHttpsConfiguration();

// Fail at startup on DI misconfiguration (missing registrations, captive scopes).
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

// --- Configuration ----------------------------------------------------------

// Bound lazily: an eager read here cannot be overridden by a test host.
var uploadSection = builder.Configuration.GetSection("Upload");

builder.Services.AddOptions<UploadLimits>()
    .Bind(uploadSection)
    .Validate(
        limits => limits.MaxFileBytes > 0 && limits.MaxRequestOverheadBytes >= 0,
        "Upload size limits must be non-negative, with MaxFileBytes greater than zero.")
    .ValidateOnStart();

// Outer guard: refuse a request before reading it. The precise per-file rule lives in the endpoint.
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize =
        checked(uploadSection.GetValue<long>("MaxFileBytes") + uploadSection.GetValue<long>("MaxRequestOverheadBytes")));

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
/// See <see href="../../docs/adr/0006-openapi-scalar.md">ADR 0006</see>.
/// </summary>
public partial class Program
{
    /// <summary>Supports type references from the integration-test host; never instantiated directly.</summary>
    protected Program()
    {
    }
}

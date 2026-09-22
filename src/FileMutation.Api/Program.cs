using System.Text.Json.Serialization;
using FileMutation.Api;
using FileMutation.Api.Endpoints;
using FileMutation.Api.ExceptionHandling;
using FileMutation.Application;
using FileMutation.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;
var builder = WebApplication.CreateSlimBuilder(args);

// CreateSlimBuilder omits HTTPS wiring to keep the startup path small. Without this call, binding
// any https:// address throws at startup — which no WebApplicationFactory test can catch, because
// TestServer never binds a real socket. See docs/adr/0006-openapi-scalar.md.
builder.WebHost.UseKestrelHttpsConfiguration();

builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

// Bound lazily rather than read here: configuration sources are still being composed at this
// point, and an eager read cannot be overridden by a test host.
var uploadSection = builder.Configuration.GetSection("Upload");

builder.Services.AddOptions<UploadLimits>()
    .Bind(uploadSection)
    .Validate(
        limits => limits.MaxFileBytes > 0 && limits.MaxRequestOverheadBytes >= 0,
        "Upload size limits must be non-negative, with MaxFileBytes greater than zero.")
    .ValidateOnStart();

// Kestrel's ceiling is the outer guard that refuses a request before it is read; the precise
// per-file rule is enforced by the endpoint against the same configuration.
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize =
        checked(uploadSection.GetValue<long>("MaxFileBytes") + uploadSection.GetValue<long>("MaxRequestOverheadBytes")));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddFileMutationInfrastructure();
builder.Services.AddSingleton<SingleFileMutationService>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonSerializerContext.Default));
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
app.UseExceptionHandler();
app.MapFileMutateEndpoint();

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

using System.Text.Json.Serialization;
using FileMutation.Api.Endpoints;
using FileMutation.Api.ExceptionHandling;
using FileMutation.Domain.Ports;
using FileMutation.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;
var builder = WebApplication.CreateSlimBuilder(args);

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
builder.Services.AddSingleton<IRandomSequenceGenerator, CryptoRandomSequenceGenerator>();
builder.Services.AddSingleton<IFileMutator, DateAndRandomSequenceMutator>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonSerializerContext.Default));
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
app.UseExceptionHandler();
app.MapFileMutateEndpoint();

app.Run();

[JsonSerializable(typeof(ProblemDetails))]
internal partial class ApiJsonSerializerContext : JsonSerializerContext;

public partial class Program;

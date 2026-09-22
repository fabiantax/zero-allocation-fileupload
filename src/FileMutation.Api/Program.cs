using System.Text.Json.Serialization;
using Scalar.AspNetCore;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonSerializerContext.Default));
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapGet("/hello", () => TypedResults.Ok(new HelloResponse("Hello, World!")))
    .WithName("GetHello");

app.Run();

public sealed record HelloResponse(string Message);

[JsonSerializable(typeof(HelloResponse))]
internal partial class ApiJsonSerializerContext : JsonSerializerContext;

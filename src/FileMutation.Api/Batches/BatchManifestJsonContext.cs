using System.Text.Json;
using System.Text.Json.Serialization;
using FileMutation.Domain.Batches;

namespace FileMutation.Api.Batches;

/// <summary>
/// Source-generated serialization for <c>manifest.json</c>: camelCase names, no reflection
/// (reflection JSON breaks native AOT, ADR 0001). Public so tests call it directly, like <see cref="BatchManifest"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(CamelCaseBatchFileStatusConverter)])]
[JsonSerializable(typeof(BatchManifest))]
public sealed partial class BatchManifestJsonContext : JsonSerializerContext;

/// <summary>
/// Writes the row status as <c>mutated</c> / <c>rejected</c>. The generic converter closes over the enum
/// at compile time, so it is trim-safe; the context's own string-enum option would keep the member casing.
/// </summary>
internal sealed class CamelCaseBatchFileStatusConverter()
    : JsonStringEnumConverter<BatchFileStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

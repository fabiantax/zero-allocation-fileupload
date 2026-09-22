# .NET implementation conventions

Settled decisions. Do not re-litigate these while implementing a task — if one looks wrong,
raise it as an ADR, don't quietly deviate.

## Solution layout

```
FileMutation.sln
src/FileMutation.Domain/          # BCL only. Value objects, ports, mutation policy
src/FileMutation.Application/     # Use cases, validation. References Domain only
src/FileMutation.Infrastructure/  # Adapters implementing Domain ports
src/FileMutation.Api/             # Minimal API host = composition root
tests/FileMutation.<Project>.Tests/    # one per src project, mirrors its name
tests/FileMutation.Architecture.Tests/ # ArchUnitNET rules
benchmarks/FileMutation.Benchmarks/    # BenchmarkDotNet, excluded from dotnet test
docs/adr/                              # numbered, immutable once merged
```

Dependencies point inward only: `Api → Application → Domain`, `Infrastructure → Domain`.
Nothing points back. Architecture tests enforce this.

## Non-negotiables

| Rule | Instead of |
|---|---|
| Inject `TimeProvider` | `DateTime.Now` / `DateTime.UtcNow` |
| Inject `IRandomSequenceGenerator` | `Random` / `RandomNumberGenerator` directly |
| Stream via `System.IO.Pipelines` | `ReadAllBytes`, `ReadAllText`, whole-file `MemoryStream` |
| Return a result for expected failures | Throwing on validation failure |
| `Results.File(..., fileDownloadName:)` | Hand-writing `Content-Disposition` |
| Built-in `AddProblemDetails()` (RFC 7807) | A custom error envelope |
| Source-generated `JsonSerializerContext` | Reflection-based JSON; Newtonsoft is banned |
| `Microsoft.AspNetCore.OpenApi` + Scalar | Swashbuckle — it blocks AOT |
| Minimal APIs | MVC controllers — not AOT-supported |

Domain never references ASP.NET Core, EF Core, `HttpContext`, `IFormFile`, or file paths.
`Task`/`CancellationToken` in Domain are fine; `HttpClient` is not.

## Accepted upload formats

Exactly one format is accepted. Widening this set is a PRD change, not an implementation choice.

| Check | Accepted | Otherwise |
|---|---|---|
| Extension | `.txt` | 415 |
| Declared content-type | `text/plain` | 415 |
| Encoding | UTF-8, BOM optional | 415 |

All three must pass. A file that decodes cleanly is **not** thereby acceptable — `.json` and
`.csv` decode fine and would be corrupted by a naive append, so they are rejected on extension.

Decode validation happens incrementally while streaming, never by materialising the file.
A multi-byte UTF-8 sequence can straddle a `ReadOnlySequence<byte>` segment boundary — carry
the partial sequence across segments; a per-segment decoder call that assumes complete
sequences will pass every test except a file that happens to split one.

## Ports have one implementation, and that is fine

`IFileMutator`, `IFileRepository` and `IRandomSequenceGenerator` each have a single adapter.
That is what ports-and-adapters looks like — a port exists to invert a dependency and keep the
core testable, so it is judged by whether the dependency needs inverting, not by how many
implementations exist.

What is still refused is a *second* port that duplicates an existing one's shape. If a new
interface takes the same inputs and returns the same outputs as one already there, extend the
existing port instead of adding a parallel one.

## Modelling

- `FileName` is a value object, validated in its constructor — it cannot exist invalid.
- **No aggregate root.** This is a stateless transformation with no identity or lifecycle.
  Do not add one to look DDD-shaped.
- The mutation **policy** (what is appended, in what format) lives in Domain as a pure
  function over `Span<byte>`. The **plumbing** (Pipelines) lives in Infrastructure and calls
  it. Keep that split — it is what makes the hot path testable without streams.
- API DTOs map at the endpoint. They never reach Domain.

## Testing

- One test project per src project, named `FileMutation.<Project>.Tests`.
- Unit tests need no ASP.NET Core types in scope. Integration tests use
  `WebApplicationFactory` and live in `FileMutation.Api.Tests`.
- Branch coverage ≥80% (branch, not line — line coverage is easy to pass without
  exercising conditionals).
- A test that would still pass with the change reverted is worse than no test.
- Benchmarks are not tests: they live in `benchmarks/`, excluded from `dotnet test` and
  from the coverage gate.

## Out of scope — do not build these

Persistence beyond the local-disk adapter, audit trails, OpenTelemetry, authentication,
rate limiting, queue ingestion, Azure Blob storage, file query/delete endpoints, GDPR/PII
handling, Gherkin tests. Each was decided deliberately; see the PRD's out-of-scope table.

If a task seems to need one of these, stop and ask — do not implement it.

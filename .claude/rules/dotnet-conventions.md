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

`IFileMutator` and `IRandomSequenceGenerator` each have a single adapter.
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

## Unwired code: find the cause before choosing the fix

**Problem**: `IMutateFileUseCase` was public with no implementation and no caller, so #24 deleted it
as dead code. The real reason it was unwired was that the endpoint had swallowed the job it should
have delegated. #31 now recreates it. The deletion removed the symptom and left the cause.

The same check on `src/FileMutation.Api/Contracts/` found two unused types with opposite answers:
`MutateFileRequest` is unwired because the endpoint declares `.Accepts<IFormFile>(...)` instead of
it, so the OpenAPI document under-describes the form — that one should be **wired**.
`MutateFileResponse` has a `Stream` property and can never be an OpenAPI schema — that one should
be **deleted**. "Remove unused code" would have been right once out of three times.

**Rule**: when a type is unused, write down *why* before deciding. There are three causes and they
have different fixes:

| Cause | Fix |
|---|---|
| The caller does the work inline that this type should own | Wire it |
| It was written for a shape the code does not actually have | Delete it |
| It is a genuine public API for another assembly | Leave it, and say so in a comment |

No analyzer can do this for you. SonarAnalyzer and `IDE0051` flag unused **private** members only;
a public type may be consumed from another assembly, so they stay silent on exactly the cases above.
Branch coverage plus an architecture test asserting every port has an implementation and a DI
registration is what catches them here.

## No InternalsVisibleTo

**Problem**: making Infrastructure adapters `internal` so the compiler enforces "the Api depends on
the port, not the adapter" appears to require opening them to the test project.

**Rule**: it does not, and `InternalsVisibleTo` is not the answer. Resolve the adapter through the
public DI extension instead:

```csharp
var provider = new ServiceCollection().AddFileMutationInfrastructure().BuildServiceProvider();
var mutator = provider.GetRequiredService<IFileMutator>();
```

This is better than the `InternalsVisibleTo` version rather than a workaround for it: the test
exercises the real registration, so a missing or wrong DI entry fails a test instead of passing one.
If a test cannot be written this way, it is coupled to the adapter rather than the port — fix that,
do not widen the assembly.

## Service lifetimes: scoped default, singleton by evidence

**Problem**: `SingleFileMutationService` was registered `AddSingleton` because it was stateless —
zero instance fields, per-call state on the disposed result. "Stateless today" is a promise about
the future, not an invariant anything enforces. The next dependency a service like this gains is
usually per-request (a DbContext, a unit of work), and then: a scoped dependency inside a
singleton is a **captive dependency** — the per-request object is pinned for app lifetime, with
stale tracked entities, connection-pool starvation, and thread races. Our `ValidateScopes` +
`ValidateOnBuild` make that a startup exception rather than silent corruption, so it fails loudly
— but the fix is still a lifetime rewrite of every consumer (#56 moved the service to scoped for
exactly this reason).

**Rule — the ladder, default first:**

| Lifetime | When |
|---|---|
| **Scoped** | The default for any service consumed by request handling. Survives the next dependency added. Cost is one small object per request — never a reason to deviate for anything that touches a request. |
| **Singleton** | Only when all three hold: (1) no mutable instance state, or state that is genuinely immutable and thread-safe; (2) every dependency is itself singleton; (3) it is an infrastructural leaf — clock, randomness, connection factory, cache. Registering singleton is a *documented claim*; the current adapters (`DateAndRandomSequenceMutator`, `CryptoRandomSequenceGenerator`, `TimeProvider.System`) meet it. |
| **Transient** | Stateful per use even within one request (a parser holding position), or a cheap stateless utility where sharing would couple callers. |

**The cross-lifetime invariant — a consumer must never outlive a dependency:**
- singleton → scoped: **fails loudly** at startup under `ValidateScopes`. Correct, but you still rewrite.
- singleton → transient: **fails silently** — `ValidateScopes` does not cover it. The transient is
  effectively pinned as singleton inside its consumer, sharing whatever state it was meant to
  reset per use. Grep registrations for this shape; nothing else catches it.
- scoped → transient: fine. transient → anything: fine.

**Pre-mortem for the next PR that adds a registration:** name the lifetime in the PR description
and why it is on its ladder rung. "It's stateless" is the start of the singleton argument, not
the end — the other two conditions must also be written down.

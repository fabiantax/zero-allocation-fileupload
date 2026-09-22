# File Mutation API

A .NET 10 REST API that accepts one UTF-8 `.txt` file, appends a newline followed by the
current UTC date and a 16-character random sequence, and returns the result as a download under
the submitted filename.

## Why it is built this way

A request body has no fixed size, so materializing it—an array, a stream copy, or a string—makes
memory per request proportional to upload size. At the configured 10 MiB ceiling, the measured
naive baseline allocates 20.9 MB for input and concatenated output; those arrays reach the
large-object heap and are collected in Gen2, so under load GC pauses and tail latency occur at a
rate an untrusted client controls through upload size and concurrency. Validation written inside
the endpoint also runs only inside HTTP. The upload is instead read through
`System.IO.Pipelines` into pooled 16 KiB segments below the roughly 85,000-byte LOH threshold,
while acceptance lives in a plain `Application` class that takes bytes and knows nothing about
ASP.NET Core. The precise claim is no large-object-heap allocation for file content and bounded
pooled memory per request: 784 B per operation at 1 KB and 256 KB, 792 B at 10 MB, flat across
sizes, though at 1 KB this path is 2.58× slower than the naive path. The full measurements are
recorded in [the allocation benchmark results](docs/benchmarks/allocation-results.md).

The operation is deliberately transient: upload, validate, mutate, return. Nothing is written to
disk or a database, and there is no storage port. Validation completes before the response starts,
so an invalid final UTF-8 byte can still produce a 415 instead of a truncated 200. The successful
response is then streamed with chunked transfer encoding.

## Run locally

Install .NET SDK `10.0.301`, then from the repository root run:

```bash
dotnet restore
dotnet run --project src/FileMutation.Api
```

Open the base URL printed by ASP.NET Core with `/scalar/v1` appended, normally
<http://localhost:5000/scalar/v1>. The generated OpenAPI document is at
<http://localhost:5000/openapi/v1.json>.

In Scalar, expand `POST /files/mutate`, select **Try it**, and upload a file in the `file` field.
The accepted input must satisfy all three rules:

- its filename ends in `.txt`;
- its multipart content type is `text/plain`; and
- its complete content is valid UTF-8.

The default maximum file size is 10 MiB. A command-line round trip is:

```bash
curl --fail-with-body \
  --form 'file=@example.txt;type=text/plain' \
  --remote-header-name \
  --remote-name \
  http://localhost:5000/files/mutate
```

The exact base URL can differ if ASP.NET Core environment variables or launch settings override
the default; use the `Now listening on` address printed by the application.

## Run in a container or Codespaces

On Windows, install Docker Desktop with the WSL2 backend. On macOS, use Docker Desktop or
OrbStack. Open the repository in VS Code and run **Dev Containers: Reopen in Container**, or use
the repository's **Open in Codespaces** button; Codespaces needs no local container runtime.

The dev container uses the official `mcr.microsoft.com/devcontainers/dotnet:1-10.0` image, runs
`dotnet restore` and `dotnet dev-certs https` on creation, and forwards the same `5080` (HTTP)
and `7080` (HTTPS) ports as the launch profiles. Once it is open, run the API from the
repository root exactly as locally:

```bash
dotnet run --project src/FileMutation.Api
```

The launch profiles behave exactly as locally: the default command listens on
<http://localhost:5080>, and adding `--launch-profile https` also listens on
<https://localhost:7080>. Open either address with `/scalar/v1` appended; the OpenAPI document is
at `/openapi/v1.json` on the same port. The generated HTTPS certificate is self-signed, so a
browser may ask you to accept it before opening the HTTPS URL. The bash pre-commit hooks are
contributor tooling and do not fire on Windows; they are not needed to review or run the code.

## Test and verify

The suites separate behavior from other claims: unit and HTTP integration tests check results,
architecture tests enforce dependency direction, branch coverage is the CI coverage metric, and
allocation benchmarks live outside the coverage-gated test run. See
[ADR 0007](docs/adr/0007-testing-strategy.md) for the boundaries and their trade-offs.

### Run the tests

```bash
dotnet test
```

### Check the public API documentation gate

```bash
dotnet build /p:GenerateDocumentationFile=true -warnaserror:CS1591
```

### Verify the coverage gate

CI gates **branch** coverage at **80%**. The per-suite Cobertura reports are merged with
ReportGenerator; source-generated code (`**/obj/**`) and the test-support project
`FileMutation.TestCommon` are kept out of the denominator, because neither is product
behaviour. The full HTML report is published as a `coverage-report` artifact on every CI run.

```bash
dotnet test FileMutation.sln --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
reportgenerator "-reports:artifacts/coverage/**/coverage.cobertura.xml" "-targetdir:artifacts/coverage-report" "-reporttypes:Html" "-filefilters:-**/obj/**" "-assemblyfilters:-FileMutation.TestCommon"
```

On a sandboxed macOS runner, prefix `dotnet` with
`DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` (see
[ADR 0001](docs/adr/0001-native-aot.md)); host configuration reload stalls startup there.

### Reproduce the benchmarks

Run `dotnet run -c Release --project benchmarks/FileMutation.Benchmarks` from the repository
root. BenchmarkDotNet runs the benchmarks in an optimised child process and writes its full
reports to `BenchmarkDotNet.Artifacts/`; the measured allocation summary is recorded in
[`docs/benchmarks/allocation-results.md`](docs/benchmarks/allocation-results.md).

## Assumptions and open questions

The source ticket leaves several details unspecified. The defaults below make the resulting
product decisions visible so they can be changed deliberately rather than discovered as accidental
behavior.

### Defaults taken

| Unspecified detail | Default taken |
|---|---|
| What “a text file” means | UTF-8 `.txt` declared as `text/plain`; structured text and other encodings are rejected |
| What data is appended | `\nyyyy-MM-dd:<sequence>`, using the UTC date and 16 random characters |
| File field and cardinality | Exactly one non-empty file part named `file`; other multipart fields are ignored |
| Maximum upload | 10 MiB for the file plus 64 KiB request-framing headroom, both configurable under `Upload` |
| Filename promise | Return the submitted safe basename; path separators, control characters, blank names, `.` and `..` are rejected |
| Response delivery | Synchronous, chunked download; no later retrieval and no persisted copy |
| Error shape | Application-generated errors before the response starts use RFC 7807 `ProblemDetails`; transport or post-start failures may terminate without one |
| Expected load | Tens of concurrent uploads, with pooled memory per request bounded by the configured file limit |
| Personal or regulated data | None assumed; no compliance or immutable-audit subsystem is included |
| Evaluation access | No authentication in the trial; a production authentication model needs an explicit decision |

The precise allocation claim is in
[ADR 0005](docs/adr/0005-streaming-allocation-strategy.md).

### Accepted format and dispatch boundary

The ticket says: *"Allow users to upload a text file"* and *"Add data like the current date and
a random character sequence to the file's content."*

**"a text file" is not a specification.** Many things decode as text. `.json`, `.csv` and `.xml`
all decode cleanly as UTF-8, so a validation rule of "does it decode?" accepts them — and
appending a date to a JSON document produces structurally invalid JSON while every check
reports success. A UTF-16 file also decodes, but our appended suffix is UTF-8 bytes, so
appending to it produces mojibake. Both are silent corruption, not loud failure.

**"data *like* the current date and a random character sequence"** — "like" means *such as*.
The mandatory requirement is "add data to the file's content"; the date and the sequence are
the ticket's own examples of what that data could be.

The product decision is therefore to accept exactly one format: `.txt`, declared `text/plain`,
decodable as UTF-8 (BOM optional). All three checks must pass. Anything else is rejected with
415, including files that decode perfectly well. The service implements exactly the two named
examples — the current UTC date and a random character sequence — through a `MutationContext`,
and widening the accepted set is a product decision rather than an implementation detail.

**No registry, and no capability enum.** Both were dropped after review. With a single accepted
format the registry restated the acceptance rule in a second place, and it pushed
filename/content-type — HTTP-shaped metadata — into a domain port. `MutationCapability` was
worse: self-reported metadata that nothing in the type system enforced, so an adapter could
simply declare `Streaming` and buffer anyway. A claim verified by nothing is not a safeguard.
Dispatch goes in when a second format actually exists, and if that format needs a different
execution model it gets its own contract rather than an enum.

The planned `FileFormat` value object was dropped for the same reason: with one accepted format,
it would merely restate the acceptance rule as a second domain representation of
filename/content-type metadata. The earlier `IFileFormatMutator` alternative had the same defect
— it took the same inputs and returned the same outputs as `IFileMutator`, differing only in
name.

| Alternative | Why not |
|---|---|
| Accept anything that decodes as text | Accepts `.json`/`.csv` and corrupts them while reporting success |
| Accept any byte stream, append blindly | Corrupts UTF-16 silently; contradicts FR-6 |
| BOM-aware suffix encoder (UTF-8/16 both accepted) | More correct across more inputs, but adds an encoder seam and branches for input the ticket never asked for — and UTF-16 without a BOM stays undetectable anyway |
| A registry resolving filename + content-type to a mutator | Restates the acceptance rule in a second place and pushes HTTP-shaped metadata into a domain port, for a single format. Added when a second one exists |
| Stream the response while validating | Cannot return 415 on a late failure — the status is already committed. This was the original design and it was wrong |
| Build the `.docx` adapter too | Breaks NFR-1/NFR-3/NFR-9, exceeds the ~400 LOC PR limit, and mutates a non-text file the ticket never asked for |

### Scope boundary and deliberate exclusions

The ticket asks for one synchronous transformation: receive a text file, append a UTC date and a
random sequence, and return it. The
[decision log](docs/decision-log.md) traced
each proposed extra back to a requirement and found none for a stored-file lifecycle, regulated
audit, production access control, traffic management, or alternate ingestion channel. The
independent design review of 2026-09-22 then found that the planned repository, registry, and
capability seams were extensions for requirements already declared out of scope. PRs #18 and #19
(`0f6d7af`, `4516930`) removed them before implementation.

The detailed exclusions remain in the PRD; this section records the rule used to draw the line.

The resulting code has one HTTP operation, one acceptance rule, one mutation port, and no
database, repository, disk adapter, mutator registry, or capability model. The boundary is:
build only behavior traceable to the upload–mutate–return contract or to making that behavior
demonstrably correct.

| Deliberate exclusion | Why it is excluded |
|---|---|
| Persistence, query/delete, retention, and indexing | These concern a stored object's identity or lifecycle, and no stored object exists |
| Compliance and data-governance subsystems | Neither a regulated context nor personal data was stated |
| Authentication, rate limiting, queue ingestion, and distributed telemetry | The evaluation ticket supplies no production topology, threat model, tenant model, or operational target from which to design them |
| A BDD toolchain | Ordinary unit, integration, and architecture tests express this small behavior directly |
| Other encodings and structured or container formats | They change the transformation semantics, not merely the plumbing; the [accepted-format boundary](#accepted-format-and-dispatch-boundary) records the narrow accepted format |

Tests, architecture checks, allocation measurements, ADRs, and the browser OpenAPI UI remain in
scope because the assignment explicitly judges approach and asks for an interactive API surface.

The consequences of that boundary are accepted:

- Every abstraction in the submitted service has a present requirement.
- A reviewer can distinguish deliberate omissions from forgotten production features.
- The service is not production-hardened: it has no authentication, rate limiting, durable audit,
  service-level telemetry, or asynchronous ingestion. Those must be designed from real deployment
  requirements before production use.
- The narrow format policy rejects inputs that people may reasonably call text. That compatibility
  is given up to avoid silently damaging formats whose structure the append operation would break.
- If the product owner changes a boundary, the relevant model and port are added then. Avoiding a
  speculative seam now may make that later change larger, but it also lets the new requirement
  determine the correct seam.

### Questions for the product owner

These are the remaining clarifications worth raising. Work proceeds with the stated default until
the answer changes it.

| Question | Default taken |
|---|---|
| Is this a regulated workflow requiring immutable audit evidence and operational tracing? | No. Nothing is persisted and no audit trail is produced |
| What concurrency and upload-size distribution should production support? | Tens of concurrent requests; 10 MiB maximum per file |
| What authentication and authorization model applies in production? | None for the evaluation endpoint; do not guess a production identity model |
| Is an empty `.txt` a valid file? | No. The current endpoint returns 400 for an empty file part |

Persistence is not an open implementation placeholder: the product decision for this version is
**no persistence**. If that decision changes, the audit/storage semantics must be designed before
adding a port; see [ADR 0008](docs/adr/0008-persistence-deferral.md).

## Design record

| Topic | Record |
|---|---|
| Decisions and corrections as the design evolved | [Decision log](docs/decision-log.md) |
| System structure and request flow | [Architecture](docs/architecture.md) |
| Native AOT: proven, then parked | [ADR 0001](docs/adr/0001-native-aot.md) |
| Scope, deliberate exclusions, and accepted formats | [Assumptions and open questions](#assumptions-and-open-questions) |
| Four-project structure, ports, and no aggregate root | [ADR 0004](docs/adr/0004-solution-structure-ddd-ports.md) |
| Streaming, buffering, and allocation claim | [ADR 0005](docs/adr/0005-streaming-allocation-strategy.md) |
| Built-in OpenAPI plus Scalar | [Decision log](docs/decision-log.md#an-openapi-ui-without-swashbuckle) |
| Test, architecture, coverage, and benchmark boundaries | [ADR 0007](docs/adr/0007-testing-strategy.md) |
| No persistence and no repository port | [ADR 0008](docs/adr/0008-persistence-deferral.md) |

An independent review on 2026-09-22 caught the two-phase ordering defect
before implementation; its adopted finding is recorded in ADR 0005.

## Contributing

Enable the repository's pre-commit checks once per clone:

```bash
git config core.hooksPath hooks
```

The hook enforces issue traceability and prevents client-identifying terms from entering this
public repository. Run the build and tests before submitting a change.

## Licence

MIT — see [LICENSE](LICENSE).

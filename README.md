# File Mutation API

A .NET 10 REST API that accepts one UTF-8 `.txt` file, appends a newline followed by the
current UTC date and a 16-character random sequence, and returns the result as a download under
the submitted filename.

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

## Run the tests

```bash
dotnet test
```

The suites separate behavior from other claims: unit and HTTP integration tests check results,
architecture tests enforce dependency direction, branch coverage is the CI coverage metric, and
allocation benchmarks live outside the coverage-gated test run. See
[ADR 0007](docs/adr/0007-testing-strategy.md) for the boundaries and their trade-offs.

To verify the public API documentation gate explicitly:

```bash
dotnet build /p:GenerateDocumentationFile=true -warnaserror:CS1591
```

## Ticket assumptions and defaults

The source ticket leaves several details unspecified. The implementation makes these defaults
explicit so they can be changed as product decisions rather than discovered as accidental
behavior:

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

The accepted-format choice is recorded in
[ADR 0009](docs/adr/0009-accepted-formats-and-mutator-dispatch.md), and the precise allocation
claim is in [ADR 0005](docs/adr/0005-streaming-allocation-strategy.md).

## Questions for the product owner

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
| Scope and deliberate exclusions | [ADR 0003](docs/adr/0003-scope-and-out-of-scope.md) |
| Four-project structure, ports, and no aggregate root | [ADR 0004](docs/adr/0004-solution-structure-ddd-ports.md) |
| Streaming, buffering, and allocation claim | [ADR 0005](docs/adr/0005-streaming-allocation-strategy.md) |
| Built-in OpenAPI plus Scalar | [ADR 0006](docs/adr/0006-openapi-scalar.md) |
| Test, architecture, coverage, and benchmark boundaries | [ADR 0007](docs/adr/0007-testing-strategy.md) |
| No persistence and no repository port | [ADR 0008](docs/adr/0008-persistence-deferral.md) |
| Accepted format and the deleted dispatch abstractions | [ADR 0009](docs/adr/0009-accepted-formats-and-mutator-dispatch.md) |

The full independent critique is retained at
including findings that changed the design.

## Contributing

Enable the repository's pre-commit checks once per clone:

```bash
git config core.hooksPath hooks
```

The hook enforces issue traceability and prevents client-identifying terms from entering this
public repository. Run the build and tests before submitting a change.

## Test coverage

CI gates **branch** coverage at **80%**. The per-suite Cobertura reports are merged with
ReportGenerator; source-generated code (`**/obj/**`) and the test-support project
`FileMutation.TestCommon` are kept out of the denominator, because neither is product
behaviour. The full HTML report is published as a `coverage-report` artifact on every CI run.

```bash
dotnet test FileMutation.sln --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
reportgenerator "-reports:artifacts/coverage/**/coverage.cobertura.xml" "-targetdir:artifacts/coverage-report" "-reporttypes:Html" "-filefilters:-**/obj/**" "-assemblyfilters:-FileMutation.TestCommon"
```

On a sandboxed macOS runner, prefix `dotnet` with `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false`
(see [`docs/adr/0001-native-aot.md`](docs/adr/0001-native-aot.md)); host configuration reload
stalls startup there.

## Reproducing the benchmarks

Run `dotnet run -c Release --project benchmarks/FileMutation.Benchmarks` from the repository
root. BenchmarkDotNet runs the benchmarks in an optimised child process and writes its full
reports to `BenchmarkDotNet.Artifacts/`; the measured allocation summary is recorded in
[`docs/benchmarks/allocation-results.md`](docs/benchmarks/allocation-results.md).

## Licence

MIT — see [LICENSE](LICENSE).

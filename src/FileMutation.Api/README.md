# FileMutation.Api

The composition root and the HTTP edge. This project decides *how* a request
becomes a `FileMutationResult` and a response — nothing else. All business
rules live one layer down in `../FileMutation.Application`.

| File | Role |
|---|---|
| `Program.cs` | Composition: Kestrel limits, `AddProblemDetails`, `AddFileMutationInfrastructure()`, OpenAPI + Scalar |
| `Endpoints/FileMutateEndpoint.cs` | `POST /files/mutate`. Two-phase: read and validate everything, then respond — a late rejection must stay a 415, never a truncated 200 (`../../docs/adr/0005-streaming-allocation-strategy.md`) |
| `Contracts/MutateFileRequest.cs` | The OpenAPI multipart schema (field `file`) |
| `ExceptionHandling/ProblemDetailsExceptionHandler.cs` | RFC 7807 for *unexpected* exceptions only |

**Never here:** validation logic, mutation policy, storage. The endpoint
parses multipart, maps the service result to a status code, and writes the
response. Upload limits bind through `IOptions<UploadLimits>` — see
`appsettings.json` and the pending consolidation in issue #44.

CI starts this host on an https address and fetches the OpenAPI document — a
gate `WebApplicationFactory` tests structurally cannot be, since TestServer
never binds a socket.

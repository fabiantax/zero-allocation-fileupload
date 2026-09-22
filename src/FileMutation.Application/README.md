# FileMutation.Application

Business rules callable without HTTP. This project references
`../FileMutation.Domain` only — an ArchUnitNET rule fails the build if an
ASP.NET Core type appears in it, so the acceptance decision and the mutation
orchestration stay usable from a console app, a queue consumer, or a test.

| File | Role |
|---|---|
| `FileAcceptance.cs` | Is this file acceptable? Bytes + declared metadata in, result out. Handles multi-segment `ReadOnlySequence<byte>` without flattening |
| `SingleFileMutationService.cs` | Read → validate → mutate for exactly one file. Returns a `FileMutationResult`; per-file failure is a reason, not an exception |
| `PooledFileContent.cs` | Bounded buffered content in pooled pipe segments |

**Never here:** `HttpContext`, `IResult`, multipart parsing, response writing.
Those belong to `../FileMutation.Api`. The service is registered as a
singleton because it holds no state — per-call data lives on the result
(`IAsyncDisposable`, disposed by the caller) and in local pipes.

A future batch endpoint composes over this layer rather than modifying it:
call the one-file service per item and collect results. See
`../../docs/adr/0005-streaming-allocation-strategy.md` for the two-phase
ordering everything here preserves.

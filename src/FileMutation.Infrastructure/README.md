# FileMutation.Infrastructure

Adapters for the Domain ports. Everything here is `internal sealed` — the
compiler, not a test, keeps `../FileMutation.Api` from naming an adapter
instead of a port. The only public surface is the DI extension:

```csharp
builder.Services.AddFileMutationInfrastructure();
```

| File | Role |
|---|---|
| `DateAndRandomSequenceMutator.cs` | `IFileMutator`: copies through `PipeReader`/`PipeWriter` in pooled 16 KiB segments, appends the suffix via `MutationPolicy` |
| `CryptoRandomSequenceGenerator.cs` | `IRandomSequenceGenerator`: unambiguous-alphabet randomness |
| `ServiceCollectionExtensions.cs` | The registration above — the single wiring point |

**Never here:** policy (what to append lives in Domain), HTTP, acceptance
rules. Tests reach these adapters through the DI extension and their ports —
there is no `InternalsVisibleTo` anywhere in the solution, by rule.

The allocation behaviour this layer exists for is measured in
`../../docs/benchmarks/allocation-results.md`.

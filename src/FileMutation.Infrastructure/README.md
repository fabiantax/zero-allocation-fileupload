# FileMutation.Infrastructure

This is the layer that keeps the promises Domain makes.

Domain (`../FileMutation.Domain`) knows the rules but is forbidden from
touching anything real — no streams, no clocks, no randomness. When it needs
something done for real, it writes the requirement down as a small interface
(a *port*): "I need something that can mutate a file stream." It cannot say
who does it or how.

An *adapter* is the class that answers that want with actual machinery.
`DateAndRandomSequenceMutator` is "the one who really appends the suffix" —
it owns the pipes, the pooled segments, the byte copying. Domain never
learned those words. Swap the machinery (a different transport, a faster
copy), and Domain does not notice: only the class behind the port changes.
That is the whole deal — rules in one layer, real work in another, and a
port as the only door between them.

Everything here is `internal sealed`, so the compiler — not a test — keeps
`../FileMutation.Api` from naming an adapter instead of its port. The only
public surface is the DI extension:

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

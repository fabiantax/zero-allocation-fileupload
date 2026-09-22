# FileMutation.Domain

The policy layer: what a file is, what gets appended, and the ports the
outside world must implement. No I/O, no framework references, nothing about
HTTP. A unit test calls into this layer with a `Span<byte>` and no host in
scope.

| File | Role |
|---|---|
| `FileName.cs` | Value object — cannot exist invalid (no paths, no control chars) |
| `MutationPolicy.cs` | Pure function over `Span<byte>`: the `\nyyyy-MM-dd:sequence` suffix |
| `MutationContext.cs` | The inputs to one policy call (UTC time + random sequence) |
| `Ports/IFileMutator.cs` | Stream-in, stream-out mutation, implemented outside |
| `Ports/IRandomSequenceGenerator.cs` | Randomness source, implemented outside |

**Never here:** ASP.NET Core types, file system access, clocks (`TimeProvider`
arrives as data), configuration. `FileMutation.Domain.csproj` references no
other project and no packages — and `ProjectReferenceRules` plus the
ArchUnitNET suite (`Domain_depends_only_on_itself_and_the_BCL`) fail the build
if that changes.

Ports live here because the dependency arrow must point this way: Domain
declares what it needs; Infrastructure (`../FileMutation.Infrastructure`)
provides it. See `../../docs/adr/0004-solution-structure-ddd-ports.md`.

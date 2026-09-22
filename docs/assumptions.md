# Assumptions and open questions

The source ticket leaves several details unspecified. This page records the defaults and the
product decisions behind them, so they can be changed rather than discovered as accidental
behavior. It is linked from the [README](../README.md#assumptions-and-open-questions).

## Defaults taken

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

## Accepted format and dispatch boundary

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

## Scope boundary and deliberate exclusions

The ticket asks for one synchronous transformation: receive a text file, append a UTC date and a
random sequence, and return it. The
[decision log](docs/decision-log.md) traced
each proposed extra back to a requirement and found none for a stored-file lifecycle, regulated
audit, production access control, traffic management, or alternate ingestion channel. The
independent design review of 2026-09-22 then found that the planned repository, registry, and
capability seams were extensions for requirements already declared out of scope. They were removed
before implementation; the [decision log](docs/decision-log.md) records the corrections.

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

# Decision log

This is the index of product decisions: what the API accepts, how it behaves, and why the
design took its shape. Each entry records the alternatives I considered rather than presenting
the winner as inevitable. PR references belong here when they exist; process and tooling notes
live in the local agent-lessons archive.

## Input contract

### Ticket examples

I read “data like the current date and a random character sequence” as an illustration, not an
exhaustive specification. I implement exactly those two values through a `MutationContext`
rather than add a second append-data abstraction, because variation alone does not justify a
seam. I considered treating the illustration as an open-ended data policy, but no second data
source exists and the broader interface would only obscure the actual choice.

### Accepted formats and encoding

I accept only `.txt`, declared `text/plain`, that decodes completely as UTF-8 with an optional
BOM. I considered decode-only acceptance, but JSON, CSV, and XML also decode as text and can be
silently corrupted by a UTF-8 suffix. I also considered a BOM-aware suffix encoder and UTF-16
transcoding, but they add unrequested branches and cannot reliably detect BOM-less UTF-16.
Incremental validation therefore carries partial UTF-8 sequences across pipe-segment boundaries
instead of treating every segment as complete.

### Container formats

I left room for another file format but did not build a `.docx` adapter. I considered
`DocumentFormat.OpenXml`, but a DOCX is a ZIP package whose central directory sits at its end,
so a correct adapter must buffer the whole file and violate the streaming and allocation
constraints. I dropped the `MutationCapability` enum alternative because self-reported streaming
metadata verifies nothing; a genuinely different execution model deserves its own contract.

### No persistence and no speculative scope

I traced persistence, audit, OpenTelemetry, authentication, rate limiting, queues, blob storage,
queries, GDPR handling, and BDD tooling back to requirements the ticket does not state. I removed
the speculative `IFileRepository` port in particular, because a port with no consumer is
scaffolding that still has to be wired, tested, and explained. The seam can return when a second
requirement exists, not before.

### Upload limits

I chose a whole-request Kestrel ceiling plus an independent byte count for the selected multipart
part, returning 413 when either limit is exceeded. I considered one shared nominal file limit,
but multipart boundaries and headers can push a valid maximum-size file over the request ceiling.
Kestrel can refuse a known `Content-Length` early, but it cannot pre-reject a chunked request
whose size is unknown. The part count is therefore real enforcement, not dead code, and the
exact-boundary case is tested. IOptions expresses the paired values once instead of leaving
Kestrel-only and parser-only constants to drift apart.

## Architecture

### Prove Native AOT first

I settled Native AOT with a one-hour publish spike before the skeleton, rather than discuss it
abstractly or fold the unknown into the task every later task depended on. I accepted per-RID
output, slower publishing, and a worse debugging story in exchange for reflection-free code and
bounded allocation pulling in the same direction. The spike made “no” an acceptable result before
the package choice became load-bearing.

### An OpenAPI UI without Swashbuckle

Generate the document with the .NET 10 built-in `Microsoft.AspNetCore.OpenApi` package and serve
the interactive browser client with `Scalar.AspNetCore`. Keep source-generated System.Text.Json
metadata for application JSON types. Expose the document at `/openapi/v1.json` and the UI at
`/scalar/v1`.

Do not add Swashbuckle. Scalar satisfies the user-facing requirement—a browser can inspect and
invoke the multipart endpoint—without making the product name “Swagger” the architecture. The
choice remains after AOT was parked because it is already proven, uses the platform document
generator, and no requirement justifies a second OpenAPI stack.

The consequences are accepted: built-in document generation reduces reliance on reflection
metadata and preserves an easier route back to Native AOT; this is a deliberate literal
deviation from “Swagger UI”; Scalar remains a third-party dependency whose upgrades must be
checked against both the document and the interactive upload/download flow; and the chosen stack
gives up the mature Swashbuckle extension ecosystem, so a future customization requirement may
require different tooling or explicit document transformers.

### A value object, not a ceremonial aggregate

I modelled the filename as a `FileName` value object and deliberately created no `UploadedFile`
aggregate. I considered the more ceremonious DDD shape, but this stateless transformation has no
identity, lifecycle, or cross-entity invariant to protect. An aggregate would add ceremony
without protecting a rule, so I chose restraint as the DDD answer.

### Policy ownership

The mutation policy is a pure function over a span in Domain, while Infrastructure owns the
`System.IO.Pipelines` mechanics that feed it. I considered embedding policy in infrastructure or
pushing streams into Domain; the first couples policy to transport and the second breaks the
dependency rule. The chosen split also lets the hot path be tested against a stack-allocated span
with no stream.

### Two-phase ordering and service shape

I read and validate the complete upload before writing any response byte. I considered
interleaved read/write streaming while validating UTF-8, but HTTP commits the status before the
body, so a late failure would turn the promised 415 into a truncated 200. One HTTP-independent
acceptance function lives in Application, takes content plus declared filename and type, and can
be called by a unit test or console application without ASP.NET Core types. I rejected
endpoint-local validation, a second acceptance policy, a mutator registry, and capability
metadata because each duplicated the rule or trusted an unverified claim. I kept
`SingleFileMutationService` as one direct, constructor-injected transformation around those
existing streams and result types rather than wrap it in a command shell.

### No mediator

I evaluated MediatR, a hand-rolled `IRequest`/`IRequestHandler` dispatcher, and Wolverine’s
in-process command surface, and adopted none of them. There is one synchronous command, no read
model, no persistence, and no independently changing query path, so mediator indirection has no
divergence to manage. MediatR adds licence review and third-party dependency cost, the
hand-rolled dispatcher creates code this repository would own for one call site, and Wolverine
brings a framework-sized host for local invocation. I kept direct constructor-injected
composition and recorded the comparison in ADR-0002 so the negative decision is evidence rather
than an unexamined default.

### Native seams

I chose `TimeProvider`, `Guid.CreateVersion7()`, built-in ProblemDetails, and
`Results.File(..., fileDownloadName:)` instead of custom `IClock`, GUID, error-envelope, and
Content-Disposition helpers. I considered the hand-written versions, but they add code and tests
while losing framework behaviour such as RFC 5987 filename encoding. The built-in seams express
the same boundary without signalling unfamiliarity with the platform.

### Ports and consumers

I retained `IFileMutator` and `IRandomSequenceGenerator` despite one implementation each because
their purpose is dependency inversion and testability, not adapter count. I considered deleting
the ports under a blanket single-implementation rule, but that heuristic targets speculative
generality rather than ports-and-adapters seams. External consumers bind to the OpenAPI/HTTP
contract rather than domain assemblies; a shared package would couple release cycles and make
internal types an unintended public API.

### Streaming and chunked response

I chose `PipeReader`/`PipeWriter` over `ReadOnlySequence<byte>` end to end, with no whole-file
array anywhere. I considered `ReadAllBytes`, but a large upload allocates the entire file on the
large-object heap for every concurrent request. Validation must finish before the response, so
the accepted content is held in pooled segments below the LOH threshold; the precise claim is no
LOH allocation and request-bounded pooled memory, not “nothing is buffered.” After acceptance,
the response streams in chunks rather than materialises a second whole-file array. The response
uses `Results.File(..., fileDownloadName:)`, while `FileName` rejects path traversal on input and
preserves the original name for download.

### Standard errors and validated composition

I chose RFC 7807 `ProblemDetails`, one `IExceptionHandler`, the built-in DI container with
`ValidateOnBuild` and `ValidateScopes`. I considered a custom error envelope, scattered
per-endpoint catches, and a third-party container; respectively, they create bespoke client
parsing, let one path leak, and add an unneeded container. Startup validation turns lifetime and
scope mistakes into failures before production. The single handler makes the error contract
structural rather than something every endpoint must remember.

### FileMutation, not FileUpload

I named the solution `FileMutation.*` rather than `FileUpload.*`. The transport-based name
collides with UI upload components and describes only how bytes arrive. Mutation names the domain
operation the service performs, so I chose the product concept over the HTTP mechanism.

## Verification-shaped design

### Deterministic edges

I injected `TimeProvider` and `IRandomSequenceGenerator`, kept the mutation policy pure, and
avoided static host-dependent state, so tests compare exact bytes rather than a loose regex. I
considered a hand-written `IClock`, but the framework seam already supplies the boundary and
adding another interface signals unfamiliarity with the platform. Testability came from removing
uncontrolled dependencies, not from adding more interfaces.

### Public documentation

I put XML documentation on public types and members, with links from the docs to the ADR that
explains each decision. I considered long self-contained comments or repository-only prose, but
XML surfaces in IDE tooltips and the generated OpenAPI document. The linked ADR keeps the full
reasoning one hop away without bloating every member.

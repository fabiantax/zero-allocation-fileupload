---
name: file-mutation-api
description: .NET 10 REST API that accepts a text file upload, appends the current date and a random character sequence, and returns the mutated file for download
status: backlog
created: 2026-09-22T10:20:12Z
---

# PRD: file-mutation-api

## Executive Summary

A .NET 10 REST API with an OpenAPI UI-enabled endpoint that accepts a text file upload, mutates its content by appending the current UTC date and a random character sequence, and returns the mutated file to the caller as a download under the original filename.

Built as a shared platform capability: the upload/mutate contract is consumed both by humans (manually, through the OpenAPI UI) and by other systems in an automated pipeline. Structured so that multiple consuming domains can depend on a stable contract without coupling to the mutation or storage implementation.

Source requirement: a client-supplied ticket, held outside this repository. This PRD is
self-contained — it restates every requirement it depends on, so nothing here relies on
reading the original.

## Problem Statement

Consuming systems need a uniform way to stamp provenance data onto text files passing through a workflow. Today there is no shared service for this: each consumer would re-implement mutation logic, with inconsistent formatting and no single place to change the rules.

The file transform itself is trivial. The engineering problem is doing it in a way that:
- survives being called concurrently by several consumers without degrading,
- can be verified (tests, coverage, allocation measurement) rather than asserted,
- and keeps the mutation and storage rules swappable as requirements firm up.

## User Stories

### US-1: Upload and receive a mutated file (primary)
**As a** consuming system (or an engineer testing manually through the OpenAPI UI)
**I want to** upload a text file and receive it back with the current date and a random character sequence appended
**So that** downstream steps receive files carrying consistent provenance data.

Acceptance criteria:
- [ ] `POST` endpoint accepts a `multipart/form-data` text file upload
- [ ] Response body is the uploaded content plus the current UTC date and a random character sequence
- [ ] Response downloads under the **original filename** (`Content-Disposition: attachment; filename="..."`)
- [ ] Endpoint is visible and invocable in the Scalar OpenAPI UI
- [ ] Round trip works for an empty file and for a file at the configured size limit

### US-2: Reject invalid uploads predictably
**As a** consuming system
**I want** uploads that are too large, of the wrong type, or malformed to fail with a clear, machine-readable error
**So that** I can handle failures without parsing prose.

Acceptance criteria:
- [ ] Upload exceeding the configured max size is rejected (HTTP 413)
- [ ] Anything outside the accepted set is rejected (HTTP 415): extension other than `.txt`, declared content-type other than `text/plain`, or content that does not decode as UTF-8
- [ ] A file whose bytes decode cleanly but whose format we do not accept (`.json`, `.csv`, `.docx`) is still rejected — decoding is necessary, not sufficient
- [ ] Missing/empty file field is rejected (HTTP 400)
- [ ] All error responses generated before the response starts are RFC 7807 `ProblemDetails`
- [ ] No unhandled exception leaks a stack trace to the caller

### US-3: Trust the implementation
**As a** reviewing engineer
**I want** the quality claims (clean layering, allocation behaviour, test coverage) to be mechanically verified
**So that** I can trust them without reading every line.

Acceptance criteria:
- [ ] Architecture rules enforced by an ArchUnitNET test suite, not by convention alone
- [ ] Branch coverage ≥80%, enforced as a CI gate and published as a report
- [ ] Allocation behaviour measured by BenchmarkDotNet `[MemoryDiagnoser]`, with results recorded
- [ ] CI blocks merge when build, tests, or the coverage gate fail

## Functional Requirements

| # | Requirement |
|---|---|
| FR-1 | `POST /files/mutate` accepts `multipart/form-data` with a single text file |
| FR-2 | Mutation appends data to the file content. The ticket asks for data *"like"* the current date and a random character sequence — read as illustrative, not exhaustive. We implement exactly those two examples; that is a deliberate choice, recorded in ADR-0009, not a transcription of a fixed spec |
| FR-3 | The mutated file is returned as a download using the original (sanitised) filename |
| FR-4 | An OpenAPI document and a browser UI (Scalar) document the endpoint, its responses, and its error shapes |
| FR-5 | Upload size is capped at a configurable limit (Kestrel + `MultipartBodyLengthLimit`) |
| FR-6 | Only `.txt` files declared `text/plain` and decodable as UTF-8 (BOM optional) are accepted. All three checks must pass; anything else is rejected with 415. **The whole upload is read and validated before any response byte is written** |
| FR-7 | Application-generated errors raised **before the response has started** return RFC 7807 `ProblemDetails`. Transport-level failures (malformed HTTP, client disconnect, cancellation) and any failure after the response body has begun terminate the exchange without a structured body — this is a framework limit, not a choice |
| FR-8 | The mutation rule sits behind a port (`IFileMutator`) so it can be replaced without touching the API or application layer |
| FR-9 | Acceptance rules live in `FileMutation.Application` as a plain function over the content and its declared metadata, returning a result. It takes no ASP.NET Core types, so it is callable from a test, a console app or a queue consumer without an HTTP request. The API is an entry point to it, not its owner |

## Non-Functional Requirements

| # | Requirement |
|---|---|
| NFR-1 | Handle tens of concurrent uploads without unbounded memory growth. Content is read through `System.IO.Pipelines` into pooled segments and never materialised as a single `byte[]` or `string`. Peak memory per request is bounded by the configured maximum upload size, and the buffers are returned to the pool |
| NFR-2 | Fully asynchronous I/O end to end |
| NFR-3 | Allocation per request measured and recorded; **no large-object-heap allocations** — pooled pipe segments keep every buffer well under the 85,000-byte LOH threshold regardless of file size |
| NFR-4 | ≥80% **branch** coverage (not line coverage), enforced in CI |
| NFR-5 | Domain and Application layers have no compile-time dependency on Infrastructure or Api |
| NFR-6 | Public members carry XML documentation; ADRs are linked from XML docs where a decision explains the code |
| NFR-7 | DI container validates on build (`ValidateOnBuild`, `ValidateScopes`) so lifetime mistakes fail at startup, not in production |
| NFR-8 | Code is testable by construction: time (`TimeProvider`) and randomness injected, mutation logic pure and host-independent |
| ~~NFR-9~~ **(parked)** | Proven feasible and deliberately not enabled — see ADR-0001. Setting `PublishAot` turns the trim/AOT analyzers on for every build and pulls ILCompiler into restore, costing a five-minute cold build; a fast inner loop is worth more right now. Originally: publishes with Native AOT (`PublishAot`): no JIT warmup, fast cold start, low resident memory per instance — and the trim-safe discipline it forces is the same one the allocation goal needs. Requires built-in `Microsoft.AspNetCore.OpenApi` + Scalar (not Swashbuckle) and source-generated JSON (not reflection-based, not Newtonsoft). Library projects set `IsAotCompatible` to enable the AOT/trim analyzers; because those emit *warnings*, CI must promote the relevant IL/RDG diagnostics to errors for them to gate anything. Publishing and running the native binary is the actual proof |

## Success Criteria

- [ ] Scalar UI reachable in a browser and the endpoint invocable from it within the first merged PR
- [ ] A file uploaded through the UI returns mutated, under its original name, in a single round trip
- [ ] CI is green: build, tests, ≥80% branch coverage, architecture rules
- [ ] BenchmarkDotNet report shows measured per-request allocation, recorded in the repo
- [x] `dotnet publish -p:PublishAot=true` succeeds; startup time and binary size recorded (ADR-0001). **AOT then parked** — proven, not enabled, to keep builds fast
- [ ] Both implementation variants (simple service, CQRS) exist and an ADR states which ships and why
- [ ] Every PR under ~400 LOC of code and traceable to an issue

## Constraints & Assumptions

**Constraints**
- .NET 10 (current LTS as of 2026-09)
- Public GitHub repository
- Every change lands via PR; `main` protected; required status checks
- PRs sized ≤~400 LOC of code; markdown exempt (`.claude/rules/github-workflow.md`)

**Assumptions** (stated because the ticket is silent — each is cheap to revisit)
- "As a user" means an automated consuming system, with manual OpenAPI UI upload also supported
- File contents carry no personal data, so no GDPR/PII handling is built
- No regulated (medical/ISO) context applies — see Open Questions
- The mutated file is returned synchronously; no requirement to retrieve it later
- A queue sits in front of this service in production, but its throttling is not this service's concern
- The client runs on Azure, so production auth would be Entra ID/JWT

## Open Questions

| # | Question | Default if unanswered |
|---|---|---|
| ~~OQ-1~~ | ~~Does the mutated file need to be persisted and retrievable later?~~ | **Answered: no.** Upload, mutate, return. Nothing is written to disk or a database. The repository port has been removed rather than left as speculative scaffolding |
| OQ-2 | Does a regulated (medical/ISO) context apply, requiring an immutable audit trail plus OpenTelemetry? | **Out of scope.** Nothing in the ticket implies a regulated domain. The repository port is shaped so an audit-backed adapter can be added later without restructuring |
| OQ-3 | Expected concurrency/volume? | "Tens concurrent" assumed; design targets no unbounded per-request allocation |
| OQ-4 | Auth model for the endpoint? | Open for the exercise; production posture documented, not implemented |

OQ-2 is the remaining high-value clarification for the product owner. OQ-1 is settled: no persistence, so no storage port, no adapter, no `data/` directory.

## Out of Scope

| Question / concern | Answer | Why out of scope |
|---|---|---|
| Querying or deleting uploaded files | Not implemented | Ticket describes upload → mutate → return only; no CRUD, listing, or lifecycle operations mentioned |
| Audit trail with immutable rows (medical/ISO compliance) | Not implemented; port left extensible | No regulated context stated in the ticket. Building a compliance subsystem for an unstated requirement is speculative scope |
| OpenTelemetry / distributed tracing | Not implemented | Only justified alongside the audit-trail requirement above; no observability requirement stated |
| Queue / ServiceBus ingestion and throttling | Assumed external | API is synchronous request/response; the ticket describes no queued ingestion |
| Any storage — database, disk, or object store | Not implemented; nothing is written anywhere | The ticket is upload → mutate → return. The mutated bytes go to the response and are then gone. No EF Core, no SQLite, no `data/` directory, and no repository port held open "just in case" |
| Rate limiting | Not implemented | No abuse or multi-tenancy concern stated; a production concern rather than an exercise one |
| Authentication / authorization | Not implemented; documented as an Entra ID/JWT decision for production | Ticket describes uploading "through the Swagger UI", implying open access for evaluation |
| GDPR / PII handling | Not implemented | No personal data mentioned in file contents; assumed plain text without PII |
| Gherkin / BDD test layer | Not implemented | Plain unit and integration tests are proportionate; BDD tooling adds process overhead without adding signal here |
| Millions-of-records index tuning (UUIDv7 keys) | Not implemented | There is no store to index — OQ-1 is answered: nothing is persisted |
| `.docx` (or other container formats) | Not implemented; the seam that would host it ships | A `.docx` is a ZIP/OPC package whose central directory sits at the end of the file, so mutating it requires buffering the whole archive — breaking NFR-1 and NFR-3 — and `DocumentFormat.OpenXml` is unlikely to survive trimming under NFR-9. It is also not a text file, so it is outside the ticket. See ADR-0009 |
| Structured text (`.json`, `.csv`, `.xml`) | Not implemented; rejected with 415 | These decode as text, so a decode-only check would accept them — and appending a date would then produce structurally invalid output that still looks like a success. Restricting to `.txt` is what keeps the validation honest |
| Non-UTF-8 encodings (UTF-16, legacy code pages) | Not implemented; rejected with 415 | The appended suffix is UTF-8 bytes. Appending them to a UTF-16 file produces mojibake, so accepting the file would corrupt it silently. A BOM-aware suffix encoder is the extension point if this is ever needed |

## Dependencies

- .NET 10 SDK
- ArchUnitNET (architecture rules), xUnit, coverlet + ReportGenerator (coverage), BenchmarkDotNet (allocation)
- GitHub Actions for CI; branch protection on `main` with required checks
- CQRS variant only: MediatR (commercial licence since v13 — free tier under $5M revenue), or a hand-rolled mediator, or Wolverine

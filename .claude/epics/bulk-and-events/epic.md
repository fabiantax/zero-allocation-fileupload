---
name: bulk-and-events
status: backlog
created: 2026-09-29T11:46:09Z
updated: 2026-09-29T11:46:09Z
github: https://github.com/fabiantax/zero-allocation-fileupload/issues/109
progress: 0%
prd: .claude/prds/file-mutation-api.md
milestone: v0.3 — Bulk upload and batch events
---

# Epic: bulk-and-events

## Overview

A self-set extension, not a client requirement. Two features on top of the unchanged
single-file API:

- **Bulk upload** (US-4): many text files in one multipart request, each mutated through the
  existing single-file service, streamed back as a ZIP whose last entry is a per-file
  manifest.
- **Batch events and a progress query** (US-5, US-6): in-process domain events after each
  file, each chunk of files, and the whole batch (or its abort), so handlers can be chained;
  an in-memory read model behind `GET /batches/{id}` as the first real listener.

Signatures every story codes against: [`contracts.md`](contracts.md).

## Why this reopens recorded decisions

| Recorded decision | What changes | Recorded in |
|---|---|---|
| ADR-0002: no mediator; "events earn their keep the moment a real listener exists" | Events arrive with a real listener (the status projection). A command mediator still does not: 3 use cases, 1 shared concern, below the ADR's own "3 use cases and 2 concerns" line. ADR-0002 stands | ADR-0012 (story 000) |
| ADR-0005 two-phase rule: validate the whole upload, then write the response | Cannot hold for a streamed batch. Request-level checks stay before the first byte; per-file outcomes move into the manifest | ADR-0011 (story 000) |
| PRD out of scope: rate limiting | A concurrency limit on the batch endpoint only: one request can now hold a worker for up to 100 files | ADR-0011 |
| ADR-0008: nothing persisted | Unchanged. The read model is in memory, bounded and expiring; events are lost on crash | ADR-0012 |

## Delivery guarantee (stated once, repeated in ADR-0012)

In-process, at-most-once, lost on crash. A slow or throwing handler never slows or fails an
upload and never stops the host. Non-terminal events may be dropped at capacity and are
counted; `BatchCompleted` and `BatchAborted` are never dropped; per-batch sequence numbers make
gaps visible. Durable chaining (outbox, broker, retries) is out of scope.

## Waves and runnable count

| Phase | Stories | Runnable when phase starts |
|---|---|---|
| 1 | 000 (ADRs + PRD), 002 (extract multipart reader) | 2 |
| 2 | 001 (event contracts + publisher) | 1 |
| 3 | 003 (dispatcher), 004 (batch session), 005 (ZIP writer) | 3 |
| 4 | 006 (batch endpoint) | 1 |
| 5 | 007 (status query + end-to-end chain test), 008 (benchmark + README) | 2 |

Series `2, 1, 3, 1, 2`. Phase 2 is a deliberate single story: it lands the Domain types every
later story compiles against, and splitting it per consumer would produce three PRs of a few
record declarations each. Phase 4 is the fan-in where the three phase-3 streams meet.

## File ownership (no overlap within a phase)

| Story | Owns |
|---|---|
| 000 | `docs/adr/0011-*.md`, `docs/adr/0012-*.md`, `.claude/prds/file-mutation-api.md`, `docs/decision-log.md` |
| 002 | `src/FileMutation.Api/Endpoints/FileMutateEndpoint.cs`, new `src/FileMutation.Api/Multipart/` |
| 001 | new `src/FileMutation.Domain/{Batches,Events}/`, new `src/FileMutation.Domain/Ports/IEventPublisher.cs`, new `src/FileMutation.Infrastructure/Events/{ChannelEventPublisher,EventBusOptions}.cs`, `src/FileMutation.Infrastructure/ServiceCollectionExtensions.cs`, new `tests/FileMutation.Architecture.Tests/EventRules.cs` |
| 003 | new `src/FileMutation.Infrastructure/Events/{EventDispatcherService,EventHandlerRegistration}.cs`, `src/FileMutation.Infrastructure/FileMutation.Infrastructure.csproj` |
| 004 | new `src/FileMutation.Application/Batches/` (except `BatchStatusProjection.cs`), new `tests/FileMutation.Application.Tests/Batches/` |
| 005 | new `src/FileMutation.Api/Batches/` |
| 006 | new `src/FileMutation.Api/Endpoints/BatchMutateEndpoint.cs`, new `src/FileMutation.Api/Contracts/MutateBatchRequest.cs`, `src/FileMutation.Api/Program.cs` |
| 007 | new `src/FileMutation.Application/Batches/BatchStatusProjection.cs`, new `src/FileMutation.Api/Endpoints/BatchStatusEndpoint.cs`, `src/FileMutation.Api/Program.cs` |
| 008 | new `benchmarks/FileMutation.Benchmarks/BatchBenchmarks.cs`, new `docs/benchmarks/batch-results.md`, `README.md` |

Each story's tests go in new files named after the story's types. `Program.cs` is owned by 006
then 007, never concurrently. `README.md` is also owned by v0.2 story #107 and bug #102: story
008 starts only after both have merged.

## Blast radius (measured 2026-09-29 on `af16d81`)

References found with `grep -rn` across `src/`, `tests/` and `benchmarks/`:

- `IFileMutator`: 6 files, including `MutationBenchmarks.cs` (the published 784-792 B figure).
  Not modified by any story.
- `FileMutateEndpoint.cs`: the only multipart parser; story 002 extracts it with zero behaviour
  change, guarded by the existing 325-line `FileMutateEndpointTests.cs`.
- Hubs: `Program.cs` (composition root) and `ServiceCollectionExtensions.cs` (checked by
  `PortRules`). Each has one owner per phase.

## Success Criteria (Technical)

- [ ] `POST /files/mutate` behaviour unchanged: every existing test passes unmodified
- [ ] `IFileMutator` and `MutationBenchmarks.cs` unchanged; 784-792 B per operation still holds
- [ ] One file in memory at a time: per-file allocation flat from 1 to 100 files (story 008)
- [ ] A throwing handler and a 5-second handler do not change upload status or latency
- [ ] Terminal events are never dropped, shown by a test at capacity
- [ ] Every new test is shown to fail against a planted defect before it is trusted
- [ ] Branch coverage not lower than before any story in this epic; no PR over ~400 LOC of code

## Estimated Effort

| Phase | Stories | Estimate |
|---|---|---|
| 1 | 000, 002 | ~2.5 h (parallel) |
| 2 | 001 | ~1.5 h |
| 3 | 003, 004, 005 | ~6 h (parallel) |
| 4 | 006 | ~2.5 h |
| 5 | 007, 008 | ~3 h (parallel) |

## Splitting decisions

Choices made while splitting the three user stories, each against a written repo rule:

| Tempting split | What the epic does instead | Rule |
|---|---|---|
| Separate test-only stories | Tests ship inside the story that needs them | `story-splitting.md` "Tests ship with their implementation" |
| `IEventHandler<T>` in Application | In Domain: Infrastructure may reference only Domain, and it dispatches to handlers | `ProjectReferenceRules` |
| Multipart parser and ZIP writer in Infrastructure | In Api: both depend on ASP.NET Core types | `LayeringRules`, `ProjectReferenceRules` |
| Each story defines the event and id types it needs | One `contracts.md` binds all stories to the same types | `story-splitting.md` "Shared contracts" |
| Publish events from `POST /files/mutate` too | The single-file path does not change; events are batch-scoped | this epic, success criteria |
| A separate sequence-number allocator | Sequence numbers belong to the batch session (one owner, no shared state) | declared file ownership |

## Tasks Created
- [ ] 000.md - ADR-0011 and ADR-0012: bulk contract, batch events and the read model (parallel: true)
- [ ] 001.md - Batch event contracts and the channel event publisher (parallel: false)
- [ ] 002.md - Extract the multipart file-part reader from the single-file endpoint (parallel: true)
- [ ] 003.md - Background event dispatcher and AddEventHandler (parallel: true)
- [ ] 004.md - Batch mutation session: per-file outcomes, chunk and batch events (parallel: true)
- [ ] 005.md - Streamed ZIP response writer with a manifest (parallel: true)
- [ ] 006.md - POST /files/mutate/batch (parallel: false)
- [ ] 007.md - Batch status read model behind GET /batches/{id} (parallel: true)
- [ ] 008.md - Batch allocation benchmark and README (parallel: true)

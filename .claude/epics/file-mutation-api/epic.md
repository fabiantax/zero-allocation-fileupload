---
name: file-mutation-api
status: backlog
created: 2026-09-22T10:20:12Z
updated: 2026-09-22T10:20:12Z
progress: 0%
prd: .claude/prds/file-mutation-api.md
github: https://github.com/fabiantax/zero-allocation-fileupload/issues/1
---

# Epic: file-mutation-api

## Overview

Ship a .NET 10 upload → mutate → download endpoint as a layered (DDD/ports-and-adapters) solution, verified by architecture tests, branch-coverage-gated unit and integration tests, and a BenchmarkDotNet allocation measurement.

Sequenced so the riskiest unknown is settled first (a 1h AOT spike), a running OpenAPI UI lands second, shared contracts third to unblock parallel work, and everything else fans out from there.

## Architecture Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Layering | `Domain` / `Application` / `Infrastructure` / `Api` | Ticket asks for DDD + Clean Code; multiple consuming domains need a contract that doesn't leak implementation |
| Architecture enforcement | ArchUnitNET for type/namespace dependency rules, **plus** a separate `.csproj` reference check | ArchUnitNET analyses type and member usage in compiled assemblies, so an unused-but-forbidden `ProjectReference` produces no type dependency and passes. The project-graph check is what closes that gap |
| Dependency rule | Domain and Application never reference Infrastructure or Api | Enforced mechanically by ArchUnitNET, not by convention |
| Streaming | `System.IO.Pipelines`, `Span<byte>`/`Memory<byte>` | NFR-1/NFR-3: no whole-file buffering, no LOH allocations under concurrency |
| Native AOT | `PublishAot=true`, minimal APIs, source-generated JSON | See "Why Native AOT" below |
| OpenAPI + UI | Built-in `Microsoft.AspNetCore.OpenApi` for the document, `Scalar.AspNetCore` for the browser UI | Swashbuckle is reflection-based and blocks AOT. Scalar serves a static shell that fetches the OpenAPI document, so the "OpenAPI UI-enabled endpoint" requirement is met without the reflection dependency |
| JSON | `System.Text.Json` source generator (`JsonSerializerContext`) | Reflection-based `System.Text.Json` is disabled by default under trimming/AOT because the required metadata cannot be relied on after trimming — so generated metadata is used for every application JSON type. Newtonsoft.Json is excluded for the same reason |
| Time | `TimeProvider` (built into .NET 8+) | Native seam; no custom `IClock` abstraction needed |
| Randomness | Injected generator behind an interface | Deterministic assertions in tests |
| Errors | Built-in `AddProblemDetails()` (RFC 7807) | Framework-native; no custom error envelope |
| Download naming | `Results.File(..., fileDownloadName:)` | Sets `Content-Disposition` including RFC 5987 encoding for free |
| Persistence | **None.** No port, no adapter, no storage | OQ-1 is answered: upload, mutate, return. A repository port with no requirement behind it is speculative scaffolding that a reviewer reads as indecision, and it forces every consumer to wire an adapter for a capability nobody asked for |
| Format dispatch | `IFileMutator` carries `Format` + `Capability`; `IFileMutatorRegistry` selects one. One adapter (UTF-8 plain text) ships | Accepted set is `.txt` only. No second port is introduced — the existing mutator port gains format identity, because a parallel interface with the same shape would be duplication. See ADR-0009 |
| Mediator | Two variants, simple ships by default | MediatR is commercial since v13; the decision itself is the deliverable, recorded in an ADR |
| DI | Default container + `ValidateOnBuild`/`ValidateScopes` | Lifetime errors fail at startup instead of in production |

## Why Native AOT

Recorded here because it is a deliberate constraint, not a default — it rules packages out.

**Reasons for:**
- **No JIT warmup.** First-request latency is predictable. Under the assumed burst profile (tens of concurrent uploads), a JIT'd host pays its tiering cost exactly when load arrives.
- **Lower resident memory per instance.** This is a shared platform capability consumed by several domains, so it scales horizontally — per-instance footprint multiplies.
- **Fast cold start.** Makes scale-to-zero container hosting viable rather than something to design around.
- **It forces reflection-free, trim-safe code.** That is a discipline worth having, but note it is *not* the same constraint as the allocation goal: AOT analyzers diagnose dynamic-code and trimming hazards, not managed allocations. Reflection-free code can allocate heavily and allocation-free code can be trim-unsafe. The two are verified separately — analyzers for AOT, BenchmarkDotNet for allocation.
- **Smaller attack surface** — no runtime code generation.

**Costs accepted:**
- Publish output is platform-specific (per-RID), so CI publishes per target.
- Reflection-based serialisation is unavailable — JSON goes through the source generator.
- Rules out Swashbuckle and Newtonsoft.Json.
- Longer publish times and a less convenient debugging story for the published artifact.

**Escape hatch:** the ticket explicitly requires a Swagger-style UI. If AOT and a working OpenAPI UI turn out to conflict in practice, the UI requirement wins and AOT is dropped, with the reason recorded in an ADR. Task 000 settles this by publishing, not by discussion, before anything is built on top.

## Domain Modelling Stance (Clean Architecture / DDD)

| Decision | Choice | Rationale |
|---|---|---|
| No ceremonial aggregate | `FileName` is a value object; there is no `UploadedFile` aggregate root | The operation is a stateless transformation with no identity, lifecycle, or invariants spanning entities. Inventing an aggregate to look DDD-shaped is cargo cult |
| Policy vs plumbing | Domain owns the mutation *rule* (what is appended and in what format) as a pure function over `Span<byte>`; Infrastructure owns the `Pipelines` mechanics that feed it | Keeps Domain I/O-free per Clean Architecture, *and* makes the hot path unit-testable against a stack-allocated span with no streams. The two goals reinforce each other |
| Port ownership | `IFileMutator` and `IRandomSequenceGenerator` live in Domain; Infrastructure implements them; dependencies point inward only | Stated as placement rather than as a principle, because the tidy rule "the consuming layer declares the port" would put `IFileMutator` in Application, which orchestrates it. It sits in Domain because the mutation contract is the domain's own vocabulary. That is a judgement call, not a law |
| Published language | Consuming domains bind to the OpenAPI/HTTP contract, never to domain types | Bounded-context boundary. Sharing domain types across teams couples their release cycle to ours |
| Mapping at the edge | API DTOs are mapped in the endpoint; they never reach Domain | Stops wire-format concerns leaking into the model |
| Expected failures aren't exceptions | Validation returns a result; exceptions are reserved for genuinely exceptional states | A rejected upload is an expected outcome. Throwing on it costs an allocation and a stack unwind on a hot path |
| Always-valid objects | `FileName` validates in its constructor and cannot exist in an invalid state | Removes defensive re-checking from every downstream caller |
| Capability is stated, not inferred | `IFileMutator` carries `MutationCapability` (`Streaming` \| `BufferedRewrite`) | A second format differs in **execution model**, not parameters: `.txt` is one forward streaming pass; a container like `.docx` must buffer the whole archive. A uniform-looking call that hides that turns "zero-allocation" into "zero-allocation for `.txt`" with no signal. Making capability explicit means an adapter that breaks NFR-1/NFR-3 has to say so |
| Format axis, not content axis | The *format* is behind an interface; the *appended data* is a parameter (`MutationContext`) | The ticket asks for data "like" a date and a random sequence — illustrative, so the content could vary. That variation is a value change, not a behaviour change, so it needs a record and not a second seam. Two seams here would be the ceremony this table exists to refuse |
| Layer folders, not feature folders | Folders follow layers at this size | Vertical slices earn their keep at scale; here they would add nesting without reducing anything |

## Technical Approach

### Backend Services

- **FileMutation.Domain** — `FileName` value object, the mutation policy as a pure span-based function, and ports (`IFileMutator`, `IRandomSequenceGenerator`). No implementations, no framework references.
- **FileMutation.Application** — use-case orchestration (`MutateFileUseCase`, or a command handler in the CQRS variant) plus validation rules (size, type, text-decodability).
- **FileMutation.Infrastructure** — adapters: `DateAndRandomSequenceMutator` (Pipelines plumbing calling the domain policy) and `CryptoRandomSequenceGenerator`.
- **FileMutation.Api** — minimal API host, OpenAPI document + Scalar UI, ProblemDetails wiring, request limits, DI composition root.

### Solution Layout

```
FileMutation.sln
src/
  FileMutation.Domain/
  FileMutation.Application/
  FileMutation.Infrastructure/
  FileMutation.Api/
tests/
  FileMutation.Domain.Tests/
  FileMutation.Application.Tests/
  FileMutation.Infrastructure.Tests/
  FileMutation.Api.Tests/              # integration, WebApplicationFactory
  FileMutation.Architecture.Tests/     # ArchUnitNET rules
benchmarks/
  FileMutation.Benchmarks/
docs/adr/
```

Test projects are named `{project}.Tests` so `*.Tests` greps cleanly and each suite's subject is unambiguous. `FileMutation.Architecture.Tests` follows the same pattern although its subject is the solution's shape rather than a single project.

### Infrastructure

- GitHub Actions: `dotnet build` + `dotnet test` + branch-coverage gate on every PR; existing `pr-title-check.yml` enforces issue traceability.
- Branch protection on `main` with those checks required (applied once, out of band).
- Coverage via `coverlet.collector` → `ReportGenerator`; benchmarks run in a dedicated job, not the coverage-gated suite.

## Implementation Strategy

Four waves, chosen so something demonstrable exists after every merge, and so the riskiest
unknown is settled before anything depends on it:

0. **Wave 0 — settle the unknown (timeboxed, 1h).** Task 000. Does AOT publish with an
   OpenAPI UI? The answer picks the package stack every later task builds on. It sits
   *outside* 001 deliberately: 001 blocks all ten other tasks, so a spike inside it would
   put the riskiest question on the critical path.
1. **Wave 1 — visible skeleton (sequential).** Task 001. OpenAPI UI in a browser, CI green,
   and the coverage gate wired at threshold 0 so it exists from the first merge rather than
   arriving halfway through.
2. **Wave 2 — contracts (sequential, unblocks everything).** Task 002. Ports and DTOs merged
   so later streams compile against a stable surface.
3. **Wave 3 — parallel fan-out.** The rest, concurrent where they touch different files.

Every task is scoped to land in a single PR of ~400 LOC of code or less. Markdown is exempt
from the cap (see `.claude/rules/github-workflow.md`), which is what lets the planning docs
land in one coherent PR rather than being split three ways to satisfy a number.

## Task Breakdown Preview

| # | Task | Wave | Parallel | Depends on | Serves | Cx |
|---|---|---|---|---|---|---|
| 000 | Spike: AOT publish with OpenAPI + Scalar | 0 | no | — | — | 3 |
| 001 | API host + OpenAPI/Scalar UI + CI skeleton | 1 | no | 000 | US-1 | 2 |
| 002 | Domain contracts and ports | 2 | no | 001 | US-1 | 4 |
| 003 | Pipelines-based mutation engine | 3 | yes | 002 | US-1 | 5 |
| 004 | Upload endpoint, validation, ProblemDetails | 3 | yes | 002 | US-1, US-2 | 3 |
| 006 | Unit tests for domain, application and infrastructure | 3 | no | 003, 004 | US-1, US-3 | 2 |
| 007 | ArchUnitNET architecture rules | 3 | yes | 002 | US-3 | 2 |
| 008 | BenchmarkDotNet allocation proof | 3 | yes | 003 | US-3 | 3 |
| 009 | Evaluate CQRS mediator options + decision ADR | 3 | yes | 002 | — | 4 |
| 010 | ADRs, README, XML doc pass | 3 | yes | 001 | US-3 | 2 |
| 011 | Native AOT publish profile + measurements | 3 | yes | 003, 004 | US-3 | 3 |
| 012 | Integration tests + raise gate to 80% | 3 | no | 003, 004, 006 | US-2, US-3 | 3 |
| 013 | Implement chosen CQRS mediator on a branch | 3 | yes | 009, 003, 004 | — | 3 |

**005 is cancelled.** OQ-1 came back "nothing is persisted", so the local-disk repository
adapter has no requirement behind it. The task and its port are removed rather than left
deferred — a port kept open for a capability that was explicitly declined is scaffolding, not
extensibility.

**011 is contingent** on 000. If the spike concludes AOT is dropped, 011 closes as
not-applicable.

## Traceability

Every task traces to a PRD user story, or is explicitly marked as not serving one. Tasks that
serve no user story are enabling work or decision artifacts — legitimate, but they must be
*declared* as such so nobody assumes a requirement is covered.

| PRD user story | Covered by | Demonstrated by |
|---|---|---|
| **US-1** Upload and receive a mutated file | 001, 002, 003, 004, 006 | 012 integration test: round trip returns mutated content under the original filename |
| **US-2** Reject invalid uploads predictably | 004, 006, 012 | 012 integration tests asserting `ProblemDetails` for 413 / 415 / 400 |
| **US-3** Trust the implementation | 006, 007, 008, 010, 011, 012 | Green CI: branch coverage ≥80%, architecture rules pass, benchmark + AOT numbers committed |
| *(none)* | 000, 009, 013 | Spike, and the CQRS decision plus its counterfactual branch |

No user story may be closed until the row's "demonstrated by" artifact exists and is green.

## Linkage model

Epic, milestone, labels, board and dependencies can all encode "which group does this belong
to". Encoded four times, they drift. Each gets exactly one job:

| Mechanism | Its single job |
|---|---|
| **Epic** (parent issue + sub-issues) | Decomposition — what this work is part of |
| **Milestone** | Delivery increment — what ships together |
| **Dependencies** (native blocked-by) | Sequencing — what must come first |
| **Project board Status** | Current state — what is in flight |
| **Labels** | Cross-cutting attributes — priority, size |

Consequence: the waves above are **dependencies, not milestones**. One milestone
("v0.1 — File mutation API") covers the whole epic; wave-milestones would restate the
dependency graph and diverge from it the first time a task is resequenced.

On sync, `depends_on` frontmatter becomes native GitHub **blocked-by** links and the epic
becomes a **parent issue** with all tasks as **sub-issues**. Without that mapping the
dependency graph exists only in local markdown, and GitHub gets a flat pile of unrelated
issues.

## Dependencies

- .NET 10 SDK available on CI runners
- Branch protection configured on `main` after the first PR reports its checks (GitHub cannot
  require a check that has never run)
- OQ-2 remains open but blocks nothing; OQ-1 is answered (no persistence) and task 005 is cancelled
- Task 000's outcome determines the package stack for 001 and whether 011 exists at all

## Success Criteria (Technical)

- [ ] OpenAPI UI reachable and endpoint invocable after task 001 merges
- [ ] `dotnet build` and `dotnet test` green on every PR
- [ ] Branch coverage ≥80%, gate fails the build below that
- [ ] ArchUnitNET rules pass; Domain/Application reference no Infrastructure or Api types
- [ ] BenchmarkDotNet report committed showing measured per-request allocation
- [ ] Both variants exist; ADR records which ships and why
- [ ] No PR exceeds ~400 LOC of code

## Estimated Effort

| Wave | Tasks | Estimate |
|---|---|---|
| 0 | 000 | ~1 h (hard timebox) |
| 1 | 001 | ~1.5 h |
| 2 | 002 | ~1 h |
| 3 | 003, 004, 006–013 | ~12 h (parallelisable) |

Total ≈ 15.5 h sequential, excluding deferred 005; materially less wall-clock with parallel
streams.

## Tasks Created
- [ ] 000.md - Spike: does Native AOT publish with OpenAPI + Scalar? (parallel: false, timeboxed 1h)
- [ ] 001.md - API host + OpenAPI/Scalar UI + CI skeleton (parallel: false)
- [ ] 002.md - Domain contracts and ports (parallel: false)
- [ ] 003.md - Pipelines-based mutation engine (parallel: true)
- [ ] 004.md - Upload endpoint, validation, ProblemDetails (parallel: true)
- [ ] 006.md - Unit tests for domain, application and infrastructure (parallel: false)
- [ ] 007.md - ArchUnitNET architecture rules (parallel: true)
- [ ] 008.md - BenchmarkDotNet allocation proof (parallel: true)
- [ ] 009.md - Evaluate CQRS mediator options and record the decision (parallel: true)
- [ ] 010.md - ADRs, README, XML documentation pass (parallel: true)
- [ ] 011.md - Native AOT publish profile + measurements (parallel: true, contingent on 000)
- [ ] 012.md - Integration tests + raise branch coverage gate to 80% (parallel: false)
- [ ] 013.md - Implement chosen CQRS mediator on a variant branch (parallel: true)

Total tasks: 13 (005 cancelled — OQ-1 answered, nothing is persisted)
Parallel tasks: 8
Sequential tasks: 5
Estimated total effort: ~15.5 hours

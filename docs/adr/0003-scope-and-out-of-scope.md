# 3. Scope and out-of-scope boundaries

## Status

**Accepted** — 2026-09-22.

## Context

The ticket asks for one synchronous transformation: receive a text file, append a UTC date and a
random sequence, and return it. The [decision log](../decision-log.md#15-everything-else-that-was-deliberately-not-built)
traced each proposed extra back to a requirement and found none for a stored-file lifecycle,
regulated audit, production access control, traffic management, or alternate ingestion channel.
The independent [design review](../reviews/2026-09-22-independent-design-review.md) then found that
the planned repository, registry, and capability seams were extensions for requirements already
declared out of scope. PRs #18 and #19 (`0f6d7af`, `4516930`) removed them before implementation.

The current code reflects that correction: there is one HTTP operation, one acceptance rule, one
mutation port, and no database, repository, disk adapter, mutator registry, or capability model.
The detailed exclusions remain in the PRD; this record explains the rule used to draw the line.

## Decision

Build only behavior traceable to the upload–mutate–return contract or to making that behavior
demonstrably correct.

Features concerned with a stored object's identity or lifecycle—persistence, query/delete,
retention, and indexing—are excluded because no stored object exists. Compliance and data
governance features are excluded because neither a regulated context nor personal data was
stated. Authentication, rate limiting, queue ingestion, and distributed telemetry are excluded
because the evaluation ticket supplies no production topology, threat model, tenant model, or
operational target from which to design them. A BDD toolchain is excluded because ordinary unit,
integration, and architecture tests express this small behavior directly. Other encodings and
structured/container formats are excluded because they change the transformation semantics, not
merely its plumbing; [ADR 0009](0009-accepted-formats-and-mutator-dispatch.md) records the narrow
accepted format.

Tests, architecture checks, allocation measurements, ADRs, and the browser OpenAPI UI stay in
scope because the assignment explicitly judges approach and asks for an interactive API surface.

## Consequences

- The submitted service is small enough that every abstraction has a present requirement.
- A reviewer can distinguish deliberate omissions from forgotten production features.
- The service is not production-hardened: it has no authentication, rate limiting, durable audit,
  service-level telemetry, or asynchronous ingestion. Those must be designed from real deployment
  requirements before production use.
- The narrow format policy rejects inputs that people may reasonably call text. That compatibility
  is given up to avoid silently damaging formats whose structure the append operation would break.
- If the product owner changes a boundary, the relevant model and port are added then; avoiding a
  speculative seam now may make that later change larger, but it also lets the new requirement
  determine the correct seam.

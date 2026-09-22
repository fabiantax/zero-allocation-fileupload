# 8. No persistence and no storage port

## Status

**Accepted** — 2026-09-22. The earlier plan to defer an adapter while retaining a repository port
is superseded.

## Context

The operation returns transformed bytes synchronously and has no requirement to retrieve them
later. An early design retained `IFileRepository` so storage could supposedly be added by swapping
an adapter. The independent design review of 2026-09-22
identified the contradiction: a required repository dependency is not optional, while an unused
one is speculative scaffolding. The [decision log](../decision-log.md#15-everything-else-that-was-deliberately-not-built)
records the correction, and PR #18 (`0f6d7af`) answered OQ-1, removed the repository and disk
adapter, and deleted the persistence task. No storage abstraction or implementation exists in the
current code.

## Decision

Persist nothing. Do not write uploaded or mutated bytes to a database, object store, temporary
file, or application-managed disk. Do not keep a repository port with a null adapter. The request
owns the upload, the response receives the mutation, and completion ends the service's interest in
both.

If an audit trail is later required, first clarify what must be durable: content or hash, actor,
timestamp, mutation parameters, outcome, retention, and retrieval/erasure rules. That requirement
would introduce identity and lifecycle concepts, an application-facing persistence port shaped by
the audit use case, a durable adapter, migrations/operations, failure semantics, and tests. It may
also require changing the success contract so a response is not reported before the required
audit record is durable. Those choices cannot be encoded honestly in a generic repository today.

## Consequences

- There is no database, schema, storage credential, cleanup job, repository wiring, or null adapter
  to operate and explain.
- Uploaded content exists only in request-scoped pooled memory and the outgoing response path; the
  service cannot retrieve, replay, list, or delete a previous mutation.
- There is no durable audit evidence. This service is unsuitable for a regulated workflow until
  the audit question is answered and implemented.
- A later persistence requirement will require a real design change rather than an adapter swap.
  That flexibility is deliberately given up because the missing semantics—not the storage API—are
  the hard part.
- Avoiding disk also means the maximum accepted upload must fit within the configured per-request
  pooled-memory bound.

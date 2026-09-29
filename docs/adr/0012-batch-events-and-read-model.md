# 12. Batch events and the batch-status read model

## Status

**Accepted**: 2026-09-29. A self-set extension (v0.3), not a client requirement.
[ADR 0002](0002-cqrs-mediator-choice.md) stands: no mediator is adopted.
[ADR 0008](0008-persistence-deferral.md) stands: nothing is persisted.

## Context

With bulk upload ([ADR 0011](0011-bulk-upload-contract.md)), other code needs to react to
progress: after one file, after a chunk of files, and after the whole batch. The ADR 0002
addendum set two tests for when this machinery earns its keep, and both are applied here as
written.

**Mediator: the count does not reach the line.** The addendum's rule is at least three use
cases *and* at least two shared cross-cutting concerns. v0.3 has three use cases (mutate one
file, mutate a batch, read batch status). Shared concerns: logging and timing, the one concern
a pipeline behavior would carry. Event publication is not one: it happens at specific points
inside the batch session, it is not something wrapped around every handler. Three and one is
below the line, so the endpoints keep calling their services directly, as today.

**Events: a real listener now exists.** The addendum says events earn their keep "the moment a
real listener exists, and no sooner." The batch-status read model below is that listener, and it
ships in the same release as the events. That is the CQRS split v0.3 adopts: a write side that
records what happened as events, and a read side built from them. It needs no mediator.

## Decision

**Events.** Five records in `FileMutation.Domain.Events`, each carrying the `BatchId`, a
per-batch `Sequence` (1, 2, 3, ... without gaps at the source) and `OccurredAt` from
`TimeProvider`:

| Event | Published when | Terminal |
|---|---|---|
| `FileMutated` | a file was accepted and mutated | no |
| `FileRejected` | a file was rejected or failed; the batch continues | no |
| `BatchChunkCompleted` | every `ChunkSize` files, plus a final partial chunk | no |
| `BatchCompleted` | every file part was processed and the archive completed | yes |
| `BatchAborted` | the batch stopped early (disconnect, malformed body, limit breach) | yes |

Exactly one terminal event is published per batch.

**Publishing.** The batch session publishes through the `IEventPublisher` port. The adapter
writes to an in-process channel and returns immediately: publishing never blocks the upload and
never throws into it.

**Delivery guarantee: in-process, at-most-once, lost on crash.** A background consumer reads
the channel and invokes every registered handler. The queue has a capacity for non-terminal
events; at capacity a non-terminal event is dropped and counted in the
`filemutation.events.dropped` metric. Terminal events are always queued, because "the batch is
done" is the event callers most need. A consumer that sees a gap in `Sequence` knows an event
was dropped.

**Handler isolation.** Every handler call runs in its own `try/catch`; a failure is logged and
counted (`filemutation.events.handler_failures`), and the next handler and the next event still
run. This matters because an exception escaping a `BackgroundService` stops the host by default
since .NET 6 (`BackgroundServiceExceptionBehavior.StopHost`): without isolation, one faulty
handler would take the API down.

**Chaining.** A handler implements `IEventHandler<TEvent>` and is registered with
`services.AddEventHandler<TEvent, THandler>()`. Registration closes one generic subscription at
compile time: no reflection, no assembly scanning, nothing the AOT analyzers would flag.

**Read model.** `BatchStatusProjection` handles all five events and keeps, per batch, state and
counts only: never file names, so one caller cannot learn another caller's files from a batch
id. It is in memory, holds at most `MaxTrackedBatches` entries (default 1,000) and forgets a
batch `TimeToLive` (default 15 minutes) after its last event. `GET /batches/{id}` serves it: 200
with the counts, 404 for an unknown or expired id, 400 for a malformed one. The id is 128
random bits, returned in the batch response's `X-Batch-Id` header. The read side is eventually
consistent: a status read right after the upload may lag the last events by milliseconds.

## Options considered

- **In-band handlers (called on the request path).** Simplest ordering, but a slow or failing
  handler would slow or fail the upload it is merely observing. Rejected.
- **MediatR notifications or Wolverine.** ADR 0002 already weighed both; nothing in v0.3 changes
  that analysis (licence for the first, a runtime-sized dependency for the second), and the
  count above does not reach the addendum's line.
- **Durable delivery: outbox, broker, retries.** The addendum reserves that for workflows that
  must survive restarts or span systems. Nothing here does, and a durable outbox would need
  storage, which ADR 0008 rules out. Out of scope, stated rather than implied.
- **Handlers that transform file content.** Events run after the upload, when the file bytes
  are already gone from memory (nothing is stored). A step that must see or change the content
  belongs inside the stream, not behind an event. Out of scope for v0.3.
- **A metrics listener as the first subscriber.** Counters alone would be a listener in name
  only; the read model is a listener a caller can use. Metrics are emitted by the publisher and
  dispatcher directly instead.

## Consequences

- Handlers can be chained after a file, a chunk or a batch without touching the endpoint or the
  batch session.
- Delivery is best-effort. A consumer that needs every event must watch for sequence gaps, and
  a process crash loses queued events. That is documented, not hidden.
- A slow handler delays later events, not uploads; the drop counter shows when it falls behind.
- The status endpoint forgets a batch after `TimeToLive`, by design: there is no history.
- A command mediator can still be revisited under ADR 0002's rule if a second shared concern
  appears, such as authorization per use case.

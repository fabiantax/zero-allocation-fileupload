# Code contracts: bulk-and-events

Every story in this epic codes against these signatures. Story 001 lands the Domain part
verbatim; later stories implement the rest. A change to anything here is a change to this
file first, in its own PR, never a silent divergence inside an implementation PR.

XML documentation is required on every public member (`CS1591` is an error), so the summaries
below are the documentation to copy, not suggestions.

## Placement rules (why each type lives where it does)

- `ProjectReferenceRules`: Infrastructure references only Domain. Anything Infrastructure
  implements or dispatches to (`IEventPublisher`, `IEventHandler<T>`, the event records) must
  therefore live in **Domain**.
- `LayeringRules.Port_interfaces_live_in_the_domain` and `PortRules`: every interface in
  `FileMutation.Domain.Ports` needs an implementation **and** a registration in
  `AddFileMutationInfrastructure`. `IEventPublisher` is a port and satisfies both.
  `IEventHandler<T>` is an open generic extension point, not a port, so it lives in
  `FileMutation.Domain.Events`, outside the namespace those rules scan.
- Failure reason names (`FileMutationFailureReason`, `FileRejectionReason`) live in
  Application; Domain cannot reference them. Events and manifest rows carry the enum member
  **name** as `ReasonCode`, produced in Application with `nameof`-safe `ToString()`.
- Api types that `FileMutation.Api.Tests` unit-tests directly are `public`, with a one-line
  comment saying why (same precedent as `Program`). The repo forbids `InternalsVisibleTo`
  (`dotnet-conventions.md`), and reaching an `internal` type through reflection turns a rename
  into a runtime test failure instead of a compile error. Infrastructure adapters stay
  `internal` and are tested through `AddFileMutationInfrastructure()`, as that rule says.
- The single-file path (`IFileMutator`, `ISingleFileMutationService`, `POST /files/mutate`)
  does not change. `IFileMutator` backs the published 784-792 B per-operation benchmark.

## Domain: `src/FileMutation.Domain/Batches/`

```csharp
namespace FileMutation.Domain.Batches;

/// <summary>Identifies one batch upload: 128 random bits as 32 lowercase hex characters.</summary>
public readonly record struct BatchId
{
    /// <summary>Gets the 32-character lowercase hex value.</summary>
    public string Value { get; }

    /// <summary>Creates an identifier from a cryptographic random source.</summary>
    public static BatchId New();              // RandomNumberGenerator.GetHexString(32, lowercase: true)

    /// <summary>Parses exactly 32 characters of [0-9a-f]; anything else fails.</summary>
    public static bool TryParse(string? value, out BatchId id);

    /// <summary>Returns <see cref="Value"/>.</summary>
    public override string ToString();
}

/// <summary>What happened to one file part of a batch.</summary>
public enum BatchFileStatus
{
    /// <summary>The file was accepted, mutated and written to the archive.</summary>
    Mutated,

    /// <summary>The file was not mutated; <see cref="BatchFileOutcome.ReasonCode"/> says why.</summary>
    Rejected,
}

/// <summary>One manifest row: metadata about one file part, never its content.</summary>
public sealed record BatchFileOutcome(
    int Index,                  // 0-based position among file parts in the request
    string? DeclaredFileName,   // as sent by the caller; may be invalid
    string? EntryName,          // archive entry name when Mutated (deduplicated), else null
    BatchFileStatus Status,
    string? ReasonCode);        // null when Mutated
```

## Domain: `src/FileMutation.Domain/Events/`

```csharp
namespace FileMutation.Domain.Events;

/// <summary>Base of every batch event. <see cref="Sequence"/> starts at 1 per batch and has no gaps
/// at the source, so a consumer that sees a gap knows an event was dropped.</summary>
public abstract record BatchEvent(BatchId BatchId, long Sequence, DateTimeOffset OccurredAt)
{
    /// <summary>Gets whether this event ends its batch. Terminal events are never dropped.</summary>
    public virtual bool IsTerminal => false;
}

/// <summary>One file was accepted and mutated. Not a delivery receipt: the client may still disconnect.</summary>
public sealed record FileMutated(BatchId BatchId, long Sequence, DateTimeOffset OccurredAt,
    int FileIndex, string EntryName) : BatchEvent(BatchId, Sequence, OccurredAt);

/// <summary>One file was rejected; the batch continues.</summary>
public sealed record FileRejected(BatchId BatchId, long Sequence, DateTimeOffset OccurredAt,
    int FileIndex, string? DeclaredFileName, string ReasonCode) : BatchEvent(BatchId, Sequence, OccurredAt);

/// <summary>A chunk of files finished: every ChunkSize files, plus a final partial chunk.</summary>
public sealed record BatchChunkCompleted(BatchId BatchId, long Sequence, DateTimeOffset OccurredAt,
    int ChunkNumber, int FilesInChunk, int FilesSoFar) : BatchEvent(BatchId, Sequence, OccurredAt);

/// <summary>Every file part was processed and the archive was completed.</summary>
public sealed record BatchCompleted(BatchId BatchId, long Sequence, DateTimeOffset OccurredAt,
    int MutatedCount, int RejectedCount) : BatchEvent(BatchId, Sequence, OccurredAt)
{
    /// <inheritdoc />
    public override bool IsTerminal => true;
}

/// <summary>The batch stopped early: client disconnect, malformed multipart, or a limit breach
/// after the response started.</summary>
public sealed record BatchAborted(BatchId BatchId, long Sequence, DateTimeOffset OccurredAt,
    int FilesProcessed, string ReasonCode) : BatchEvent(BatchId, Sequence, OccurredAt)
{
    /// <inheritdoc />
    public override bool IsTerminal => true;
}

/// <summary>Reacts to one event type. Runs on a background consumer, never on the request path.
/// A throwing handler is logged and counted; it cannot fail the upload or stop the host.</summary>
public interface IEventHandler<in TEvent> where TEvent : BatchEvent
{
    /// <summary>Handles one event.</summary>
    ValueTask HandleAsync(TEvent batchEvent, CancellationToken cancellationToken);
}
```

## Domain: `src/FileMutation.Domain/Ports/IEventPublisher.cs`

```csharp
namespace FileMutation.Domain.Ports;

/// <summary>Hands batch events to the in-process consumer. Delivery is at-most-once and does not
/// survive a process crash (ADR-0012).</summary>
public interface IEventPublisher
{
    /// <summary>Queues an event without blocking and without throwing. Returns false only when a
    /// non-terminal event was dropped because the queue is at capacity; terminal events are
    /// always queued.</summary>
    bool TryPublish(BatchEvent batchEvent);
}
```

## Application: `src/FileMutation.Application/Batches/`

```csharp
namespace FileMutation.Application.Batches;

/// <summary>Per-batch limits, supplied by the host from configuration.</summary>
public sealed record BatchLimits(int MaxFiles, long MaxFileBytes, int ChunkSize);

/// <summary>Starts batch sessions. One session per request.</summary>
public interface IBatchMutationService
{
    /// <summary>Starts a batch and assigns its <see cref="BatchId"/>. Publishes nothing yet.</summary>
    BatchMutationSession Start(BatchLimits limits);
}

/// <summary>Drives one batch file by file. Holds outcome metadata only; file content is owned by
/// the returned <see cref="BatchFileResult"/> and released when it is disposed.</summary>
public sealed class BatchMutationSession : IAsyncDisposable
{
    /// <summary>Gets the batch identifier.</summary>
    public BatchId Id { get; }

    /// <summary>Gets one outcome per file part seen so far, in request order.</summary>
    public IReadOnlyList<BatchFileOutcome> Outcomes { get; }

    /// <summary>Validates and mutates the next file through ISingleFileMutationService.
    /// Files beyond MaxFiles are rejected with ReasonCode "BatchFileLimitExceeded".
    /// Publishes FileMutated or FileRejected, and BatchChunkCompleted every ChunkSize files.</summary>
    public Task<BatchFileResult> MutateNextAsync(Stream content, string? declaredFileName,
        string? declaredContentType, CancellationToken cancellationToken);

    /// <summary>Publishes the final partial BatchChunkCompleted (if any) and BatchCompleted.</summary>
    public void Complete();

    /// <summary>Publishes BatchAborted. Idempotent; ignored after Complete.</summary>
    public void Abort(string reasonCode);

    /// <summary>Aborts with ReasonCode "Disposed" when neither Complete nor Abort ran.</summary>
    public ValueTask DisposeAsync();
}

/// <summary>The outcome of one file plus, when mutated, its pooled content.</summary>
public sealed class BatchFileResult : IAsyncDisposable
{
    /// <summary>Gets the manifest row for this file.</summary>
    public BatchFileOutcome Outcome { get; }

    /// <summary>Gets the mutated content. Throws unless Outcome.Status is Mutated.</summary>
    public Stream Content { get; }

    /// <summary>Returns pooled segments to their pool.</summary>
    public ValueTask DisposeAsync();
}
```

Entry-name rule: the first `a.txt` keeps its name; later ones become `a (2).txt`, `a (3).txt`,
compared ordinal-ignore-case. The chosen name goes into `EntryName` and `FileMutated`.

## Application: `src/FileMutation.Application/Batches/BatchStatusProjection.cs` (story 007)

```csharp
/// <summary>Where a batch is in its lifecycle.</summary>
public enum BatchState { Running, Completed, Aborted }

/// <summary>Counts only; never file names (callers must not see each other's files).</summary>
public sealed record BatchStatus(BatchId Id, BatchState State, int Mutated, int Rejected,
    int ChunksCompleted, long LastSequence);

/// <summary>The query side: an in-memory read model built from batch events. Bounded by
/// MaxTrackedBatches; entries expire TimeToLive after their last event (TimeProvider).</summary>
public sealed class BatchStatusProjection :
    IEventHandler<FileMutated>, IEventHandler<FileRejected>, IEventHandler<BatchChunkCompleted>,
    IEventHandler<BatchCompleted>, IEventHandler<BatchAborted>
{
    public BatchStatusProjection(TimeProvider timeProvider, BatchStatusOptions options);
    public bool TryGet(BatchId id, out BatchStatus status);
}

public sealed class BatchStatusOptions   // IOptions-bindable
{
    public int MaxTrackedBatches { get; set; } = 1_000;
    public TimeSpan TimeToLive { get; set; } = TimeSpan.FromMinutes(15);
}
```

## Infrastructure: `src/FileMutation.Infrastructure/Events/` (stories 001 and 003)

```csharp
/// <summary>Capacity of the non-terminal part of the event queue.</summary>
public sealed class EventBusOptions { public int Capacity { get; set; } = 1_024; }

// story 001: internal sealed class ChannelEventPublisher : IEventPublisher
//   one unbounded Channel<BatchEvent> (single FIFO keeps per-batch order) plus an
//   Interlocked count of queued non-terminal events; at Capacity a non-terminal event is
//   dropped and counted, a terminal event is always written.

// story 003:
public static class EventHandlerRegistration
{
    /// <summary>Registers THandler as a singleton (TryAddSingleton, so one instance serves every
    /// event type it subscribes to) and subscribes it to TEvent. No reflection, no assembly
    /// scanning: each call closes one generic registration at compile time. The first call also
    /// registers the dispatcher hosted service (TryAddEnumerable).</summary>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : BatchEvent
        where THandler : class, IEventHandler<TEvent>;
}
// internal sealed class EventDispatcherService : BackgroundService
//   reads the channel; for each event invokes every matching registration inside its own
//   try/catch (BackgroundService's default is StopHost: one escaped exception would stop the API).
```

## Api (stories 002, 005, 006, 007)

```csharp
// story 002, src/FileMutation.Api/Multipart/MultipartFileParts.cs
public readonly record struct MultipartFilePart(Stream Body, string FileName, string? ContentType);

public static class MultipartFileParts
{
    public static bool TryGetBoundary(string? contentType, out string boundary);

    /// Yields form-data parts named fieldName that carry a file name; skips the rest.
    /// InvalidDataException / IOException propagate to the caller unchanged.
    public static IAsyncEnumerable<MultipartFilePart> ReadAsync(
        Stream body, string boundary, string fieldName, CancellationToken cancellationToken);
}

// story 005, src/FileMutation.Api/Batches/ZipBatchResponseWriter.cs
public sealed class ZipBatchResponseWriter : IAsyncDisposable
{
    public static ValueTask<ZipBatchResponseWriter> CreateAsync(Stream responseBody, CancellationToken ct);
    public ValueTask WriteEntryAsync(string entryName, Stream content, CancellationToken ct);
    public ValueTask WriteManifestAsync(BatchId id, IReadOnlyList<BatchFileOutcome> outcomes,
        bool complete, CancellationToken ct);    // always the last entry, "manifest.json"
    public ValueTask DisposeAsync();              // central directory written asynchronously
}
```

HTTP surface:

| Method and route | Success | Request-level errors (before the first byte) |
|---|---|---|
| `POST /files/mutate/batch`, multipart field `files` (repeated) | 200 `application/zip`, header `X-Batch-Id`, `Content-Disposition: attachment; filename="batch-<id>.zip"` | 400 not multipart / no file parts, 413 Content-Length over MaxBatchBytes, 429 concurrency limit |
| `GET /batches/{id}` | 200 `BatchStatus` JSON | 400 malformed id, 404 unknown or expired |

`manifest.json` (camelCase, STJ source-generated):

```json
{
  "batchId": "3f9c…",
  "complete": true,
  "mutated": 2,
  "rejected": 1,
  "files": [
    { "index": 0, "declaredFileName": "a.txt", "entryName": "a.txt", "status": "mutated", "reasonCode": null },
    { "index": 1, "declaredFileName": "a.txt", "entryName": "a (2).txt", "status": "mutated", "reasonCode": null },
    { "index": 2, "declaredFileName": "b.json", "entryName": null, "status": "rejected", "reasonCode": "UnsupportedFileExtension" }
  ]
}
```

A response without `manifest.json` as its last entry is truncated by definition. A file
named `manifest.json` can never collide: only `.txt` files are accepted.

## Configuration (`Upload` section, bound in Program.cs)

| Key | Default | Enforced by |
|---|---|---|
| `MaxFileBytes` | 10 MiB (unchanged) | Application, per file |
| `MaxFilesPerBatch` | 100 | Application (`BatchFileLimitExceeded` rows) |
| `MaxBatchBytes` | 100 MiB | Api: Content-Length check + per-endpoint `IHttpMaxRequestBodySizeFeature` |
| `ChunkSize` | 10 | Application |
| `MaxConcurrentBatches` | 4 | Api: built-in concurrency rate limiter, 429 |

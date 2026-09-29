# 11. Bulk upload contract

## Status

**Accepted**: 2026-09-29. A self-set extension (v0.3), not a client requirement. Amends the
two-phase rule of [ADR 0005](0005-streaming-allocation-strategy.md) for the batch endpoint only;
`POST /files/mutate` keeps it unchanged.

## Context

A caller wants to send many text files in one request and get every one back mutated. The
single-file endpoint answers with one status code for one file: ADR 0005 validates the whole
upload before the first response byte, so a rejection can still be a 413, 415 or 400.

That rule cannot hold for a batch that streams. Holding every file until all are validated
would buffer the whole batch, which is exactly the memory profile ADR 0005 was written to
avoid. Streaming each file as soon as it is ready means the status line is committed after the
first file, so a later file can no longer change it. A batch therefore needs a way to report
per-file outcomes that is not the HTTP status.

## Decision

`POST /files/mutate/batch` accepts `multipart/form-data` with a repeated `files` field. The
response is `application/zip`, streamed: one entry per accepted file, written as soon as that
file is mutated, then `manifest.json` as the last entry. Each file passes through the existing
single-file service, so acceptance rules, limits and the mutation are identical to
`POST /files/mutate`.

**Two error levels.** Request-level problems are decided before the first byte and returned as
`ProblemDetails`: not multipart (400), no file parts (400), `Content-Length` above the batch
limit (413), too many concurrent batches (429). File-level problems never change the status:
a rejected or failed file becomes a manifest row with a reason code, and the other files still
arrive. A file that fails *mutation* (a 500 on the single-file endpoint) is also a row: one
broken file must not cost the caller the rest.

**The manifest is the completion signal.** It lists every file part in request order with its
declared name, archive entry name, status and reason code, plus `complete`. A response whose
last entry is not `manifest.json` was cut off. After the status is committed, a malformed
multipart body, a client disconnect or a limit breach ends the batch: the archive is closed
with `complete: false` where the connection still allows it, otherwise the connection is
aborted. No `ProblemDetails` is attempted after the first byte.

**Names.** Entry names come from the validated `FileName`, which already rejects path
separators, `.` and `..`, so an entry cannot escape the extraction directory. Duplicates are
renamed `a (2).txt`, `a (3).txt` (ordinal, case-insensitive). `manifest.json` cannot collide
because only `.txt` is accepted.

**Limits** (the `Upload` configuration section):

| Key | Default | Checked |
|---|---|---|
| `MaxFileBytes` | 10 MiB (unchanged) | per file, as today |
| `MaxFilesPerBatch` | 100 | per file part; extra parts become `BatchFileLimitExceeded` rows without being read |
| `MaxBatchBytes` | 100 MiB | `Content-Length` up front, and the endpoint's own request-body limit |
| `ChunkSize` | 10 | chunk events, see ADR 0012 |
| `MaxConcurrentBatches` | 4 | built-in concurrency limiter on this endpoint only, 429 when full |

Kestrel's global body limit stays at one file plus overhead; only the batch endpoint raises its
own limit.

**Concurrency limit.** The PRD listed rate limiting as out of scope because one request held at
most one file. A batch request can hold a worker for up to 100 files, so the batch endpoint gets
the built-in `Microsoft.AspNetCore.RateLimiting` concurrency limiter with no queue. The
single-file endpoint is unchanged.

**Memory.** One file is in memory at a time: the same pooled two-phase path as ADR 0005 per
file, then the ZIP entry is written and the file's segments return to the pool before the next
part is read. Only per-file outcome metadata accumulates.

## Options considered

- **Accept all, then respond (full two-phase for the batch).** Keeps a single status code but
  buffers up to `MaxBatchBytes` per request. Rejected: it multiplies the pool pressure ADR 0005
  bounds per file by the file count.
- **`202 Accepted` plus a later download.** Clean status semantics, but the mutated files must
  live somewhere between the two requests. Rejected by [ADR 0008](0008-persistence-deferral.md):
  nothing is persisted.
- **NDJSON or `multipart/mixed` response.** Streams naturally, but no client or browser saves it
  as files, and the OpenAPI UI cannot download it usefully. A ZIP is what a person expects when
  asking for many files back.
- **All-or-nothing.** Simpler to describe, but one bad file among a hundred would cost the
  caller the other ninety-nine. Per-file outcomes are the point of a batch.

## Consequences

- A `200` means "the batch was accepted and processed as far as the manifest says", not "every
  file succeeded". Callers must read the manifest; the OpenAPI description says so.
- Clients that only check the status code will miss rejected files. The manifest's counts
  (`mutated`, `rejected`) make the check a one-liner.
- The archive is compressed, so the batch path allocates Deflate buffers the single-file path
  does not. Those bytes are reported separately from the 784-792 B per-operation figure, never
  folded into it.
- A disconnect after the first entry leaves the caller with a truncated archive and no
  manifest; that is detectable, not silent.
- `IFileMutator`, `ISingleFileMutationService` and `POST /files/mutate` do not change.

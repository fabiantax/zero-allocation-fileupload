# 5. Streaming and allocation strategy

## Status

**Accepted** — 2026-09-22.

## Context

UTF-8 validity can be known only after the entire upload has been read, while an HTTP 415 can be
chosen only before response bytes commit the status line. The independent
[design review](../reviews/2026-09-22-independent-design-review.md) found the original interleaved
read/write design could produce only a truncated 200 on a late validation failure. PR #19
(`4516930`) therefore established a two-phase order and corrected the allocation claim. PR #23
(`15c711c`) implemented pooled 16 KiB pipeline segments; PR #24 (`4a59553`) implemented the
two-phase endpoint, explicit selected-part byte counting, and a deliberately chunked response.
The reasoning is also recorded in the [decision log](../decision-log.md#210-streaming-async-and-what-zero-allocation-actually-claims).

## Decision

Phase 1 reads the complete selected multipart part into a `Pipe`, counts its bytes against the
configured file limit, and validates filename, content type, and UTF-8 before touching the
response. The pipe uses pooled 16 KiB segments, each below the approximately 85,000-byte
large-object-heap threshold. Multi-segment UTF-8 validation carries decoder state across segment
boundaries and rents a fixed-size character buffer.

Phase 2 exposes the validated pipe as a stream. `DateAndRandomSequenceMutator` copies it through
`PipeReader`/`PipeWriter`, appends the suffix, and writes directly to the response. The response
is chunked: the service does not materialize a second mutated copy merely to calculate
`Content-Length`.

The precise claim is **no large-object-heap allocation for file content and bounded pooled memory
per request**, not “nothing is buffered” or “the whole request allocates nothing.” The bound is
the configured maximum file size plus fixed-size pipeline/decoder buffers and framework overhead.
Kestrel supplies a whole-request ceiling where it can; the endpoint independently counts the
file part because chunked requests cannot be rejected from `Content-Length` and parser limits
are enforced during reading.

## Consequences

- All validation failures can return the intended status before the response starts.
- No file-sized `byte[]` or `string` is created, and buffer segments remain below the LOH
  threshold.
- A valid response starts only after the full upload has arrived and been validated, increasing
  time-to-first-byte compared with interleaved streaming.
- The upload is buffered—using pooled segments—for the duration of validation. Concurrent maximum-
  size uploads therefore create bounded but still material pool pressure.
- Chunked transfer gives up `Content-Length`, which can make progress reporting and some proxy
  behaviors less convenient.
- Once phase 2 starts, a mutation or transport failure cannot be replaced by `ProblemDetails`;
  the connection may terminate after a partial successful response.
- Pipelines and explicit ownership/completion are more complex than `ReadAllBytesAsync`.
- The allocation claim is now measured rather than inferred. PR #26 recorded 784 B per operation
  at 1 KB and 256 KB and 792 B at 10 MB, against 20,971,880 B and Gen2 collections for a naive
  `ReadAllBytes` baseline — see [the benchmark results](../benchmarks/allocation-results.md).
  The same run shows the cost: below roughly 256 KB the pipeline is *slower* than the naive path
  (2.58x at 1 KB), so this design is chosen for uploads near the configured ceiling, not for
  small ones.

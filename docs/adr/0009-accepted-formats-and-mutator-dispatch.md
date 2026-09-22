# 9. Accepted upload formats and mutator dispatch

Status: accepted
Date: 2026-09-22

## Context

The ticket says: *"Allow users to upload a text file"* and *"Add data like the current date and
a random character sequence to the file's content."*

Two things in that wording need a decision rather than a transcription.

**"a text file" is not a specification.** Many things decode as text. `.json`, `.csv` and `.xml`
all decode cleanly as UTF-8, so a validation rule of "does it decode?" accepts them — and
appending a date to a JSON document produces structurally invalid JSON while every check
reports success. A UTF-16 file also decodes, but our appended suffix is UTF-8 bytes, so
appending to it produces mojibake. Both are silent corruption, not loud failure.

**"data *like* the current date and a random character sequence"** — "like" means *such as*.
The mandatory requirement is "add data to the file's content"; the date and the sequence are
the ticket's own examples of what that data could be.

A third question follows from the first: if other formats might arrive later — `.docx` is the
obvious candidate — what shape should accommodate them?

## Decision

**Accept exactly one format.** `.txt`, declared `text/plain`, decodable as UTF-8 (BOM
optional). All three checks must pass. Anything else is rejected with 415, including files that
decode perfectly well.

**Implement exactly the two examples the ticket names** — current UTC date and a random
character sequence — and record that as a choice. The values live in a `MutationContext`
parameter rather than as literals inside the policy.

**Validate the whole upload before writing any response byte.** Read the request through a
`Pipe` into pooled segments, run the acceptance rule over it, and only then write the response.

**The acceptance rule lives in `FileMutation.Application`** as a plain function over the content
and its declared metadata, returning a result. It references no ASP.NET Core types. The API
parses multipart, calls it, and maps the result to a status code — the API is an entry point to
the rule, not its owner.

## Consequences

**This is what makes 415 possible at all.** An earlier version of this design streamed mutated
bytes to the response while still validating, then promised a `ProblemDetails` on failure. That
cannot work: once a response byte is flushed the status line is committed, so a late rejection
arrives as a truncated 200. Reading and deciding before writing is not merely tidier — it is the
only ordering in which the stated error contract is achievable.

**Buffering is bounded, and it is not an LOH allocation.** The content is held in pooled `Pipe`
segments, each far below the 85,000-byte large-object-heap threshold, so a 10 MB upload never
produces a 10 MB array. Peak memory per request is bounded by the configured maximum upload
size and returned to the pool afterwards. The honest claim is therefore *"no LOH allocation, and
bounded pooled memory per request"* — not *"nothing is ever buffered"*.

**No registry, and no capability enum.** Both were dropped after review. With a single accepted
format the registry restated the acceptance rule in a second place, and it pushed
filename/content-type — HTTP-shaped metadata — into a domain port. `MutationCapability` was
worse: self-reported metadata that nothing in the type system enforced, so an adapter could
simply declare `Streaming` and buffer anyway. A claim verified by nothing is not a safeguard.
Dispatch goes in when a second format actually exists, and if that format needs a different
execution model it gets its own contract rather than an enum.

**`.docx` is specified and not built.** It would need seekable storage or buffering through the
OpenXML SDK, which is disproportionate here, and it is not a text file, so it is outside the
ticket. Note the accurate form: that is a constraint of the chosen libraries, not a law of the
ZIP format — `ZipArchiveMode.Create` can write to a non-seekable stream.

**Validation cost.** Decoding still happens incrementally as segments arrive, which means
carrying partial UTF-8 sequences across `ReadOnlySequence<byte>` segment boundaries. A decoder
called per segment as though each were complete will reject valid files — and only those that
happen to split a multi-byte character, so it survives casual testing.

**Widening the accepted set is a PRD change**, not an implementation decision.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Accept anything that decodes as text | Accepts `.json`/`.csv` and corrupts them while reporting success |
| Accept any byte stream, append blindly | Corrupts UTF-16 silently; contradicts FR-6 |
| BOM-aware suffix encoder (UTF-8/16 both accepted) | More correct across more inputs, but adds an encoder seam and branches for input the ticket never asked for — and UTF-16 without a BOM stays undetectable anyway |
| A registry resolving filename + content-type to a mutator | Restates the acceptance rule in a second place and pushes HTTP-shaped metadata into a domain port, for a single format. Added when a second one exists |
| Stream the response while validating | Cannot return 415 on a late failure — the status is already committed. This was the original design and it was wrong |
| Build the `.docx` adapter too | Breaks NFR-1/NFR-3/NFR-9, exceeds the ~400 LOC PR limit, and mutates a non-text file the ticket never asked for |

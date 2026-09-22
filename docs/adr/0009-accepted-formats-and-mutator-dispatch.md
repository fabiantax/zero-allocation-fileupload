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
optional). All three checks must pass. Anything else is rejected with 415, including files
that decode perfectly well.

**Implement exactly the two examples the ticket names** — current UTC date and a random
character sequence — and record that as a choice. The values live in a `MutationContext`
parameter rather than as literals inside the policy, so varying them is a value change. We do
**not** add a seam on the content axis: "what data gets appended" varying is not a reason for
an abstraction.

**Put format identity on the existing port.** `IFileMutator` gains `Format` and
`MutationCapability`; `IFileMutatorRegistry` resolves filename + content-type to one adapter
and returns a result — never a fallback — when no adapter matches.

`MutationCapability` is `Streaming` or `BufferedRewrite`. The plain-text adapter is
`Streaming`.

## Consequences

**A second mutator port was considered and rejected.** An `IFileFormatMutator` alongside
`IFileMutator` would have taken the same inputs and returned the same outputs, differing only
in name. Format identity belongs on the port that already exists.

**The registry has one entry today, and that is not a defect.** Every port in this solution
has one adapter; that is what ports-and-adapters looks like. What the registry buys is that
format resolution has one home and cannot silently fall back — an unmatched format is a
rejection by construction, not by remembering to check.

**`MutationCapability` exists so a future adapter cannot lie.** A `.docx` adapter must buffer
the whole archive, because a ZIP central directory sits at the end of the file and cannot be
rewritten in one forward pass. That breaks NFR-1 (no whole-file buffering) and NFR-3 (no LOH
allocations — anything over ~85,000 bytes lands there), and `DocumentFormat.OpenXml` is
unlikely to survive trimming under NFR-9. If every adapter presented a uniform interface, the
"zero-allocation" claim would quietly become "zero-allocation for `.txt`" with nothing in the
type system to say so. Declaring `BufferedRewrite` forces that admission.

**`.docx` is therefore specified and not built.** It is also not a text file, so it is outside
the ticket. See the PRD's out-of-scope table.

**Validation cost.** Decoding must happen incrementally as segments arrive, which means
carrying partial UTF-8 sequences across `ReadOnlySequence<byte>` segment boundaries. A decoder
called per segment as though each were complete will reject valid files — and only those that
happen to split a multi-byte character, so it passes casual testing.

**Widening the accepted set is a PRD change**, not an implementation decision.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Accept anything that decodes as text | Accepts `.json`/`.csv` and corrupts them while reporting success |
| Accept any byte stream, append blindly | Corrupts UTF-16 silently; contradicts FR-6 |
| BOM-aware suffix encoder (UTF-8/16 both accepted) | More correct across more inputs, but adds an encoder seam and branches for input the ticket never asked for — and UTF-16 without a BOM stays undetectable anyway |
| No registry; `if` on extension at the endpoint | Works for one format, but puts format knowledge in the HTTP layer and makes a silent fallback the easy mistake |
| Build the `.docx` adapter too | Breaks NFR-1/NFR-3/NFR-9, exceeds the ~400 LOC PR limit, and mutates a non-text file the ticket never asked for |

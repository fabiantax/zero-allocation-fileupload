# Decision log

Design and implementation decisions, and the reasoning behind each. The ADRs carry the weighty
ones in full; this catches the smaller decisions that would otherwise survive only as a table
row reading like a conclusion with no visible reasoning.

Written during planning, before the work started, so the reasoning is recorded while fresh
rather than reconstructed afterwards.

**Format:** what was asked → what was decided → why → where it lives now.

Scope of this document is architecture, design and code. Repository workflow conventions —
branch protection, PR sizing, review gates, issue traceability — live in `.claude/rules/` with
their own rationale, and are not repeated here.

---

## 1. Scope: what the ticket actually asks for

### 1.1 "Add data *like* the current date and a random character sequence"

**Asked:** is that wording an exhaustive specification?

**Decided:** no — "like" means *such as*. The mandatory requirement is "add data to the file's
content"; the date and the sequence are the ticket's own examples. We implement exactly those
two, and record that as a choice.

**Why:** implementing the two named examples is the only reading that is unambiguously
compliant. But transcribing an illustration as if it were a spec hides that a choice was made.
The values live in a `MutationContext` parameter rather than as literals inside the policy, so
varying them is a value change — which is also why there is **no second abstraction** on the
"what data gets appended" axis. Something varying is not by itself a reason for a seam.

**Where:** PRD FR-2, ADR-0009.

### 1.2 Which files count as "a text file"

**Asked:** the ticket says "a text file" — which ones do we accept?

**Decided:** `.txt`, declared `text/plain`, decodable as UTF-8 (BOM optional). All three must
pass. Everything else is 415.

**Why:** "does it decode as text?" is the obvious rule and it is wrong. `.json`, `.csv` and
`.xml` all decode cleanly, so that check *passes* — and appending a date to a JSON document
produces structurally invalid JSON while every validation reports success. Silent corruption
with a green light is worse than a loud rejection. Restricting on extension is what keeps the
validation honest.

**Where:** PRD FR-6, ADR-0009, `.claude/rules/dotnet-conventions.md`.

### 1.3 Encoding

**Asked:** which encodings?

**Decided:** UTF-8 only. UTF-16 and legacy code pages are rejected with 415.

**Why:** the appended suffix is UTF-8 bytes. Appending them to a UTF-16 file produces mojibake
— again, corruption that looks like success. A BOM-aware suffix encoder would handle more
inputs and is the natural extension point, but it adds branches for input the ticket never
asked for, and UTF-16 without a BOM stays undetectable regardless.

**Consequence accepted:** decode validation has to happen incrementally while streaming, which
means carrying partial UTF-8 sequences across `ReadOnlySequence<byte>` segment boundaries. A
decoder called per segment as though each were complete rejects valid files — and only those
that happen to split a multi-byte character, so it survives casual testing.

**Where:** ADR-0009, task 004.

### 1.4 `.docx` and other container formats

**Asked:** can we process different file types differently — a separate handler for `.docx`?

**Decided:** build the seam, do not build the `.docx` adapter.

**Why:** a `.docx` is a ZIP/OPC package whose central directory sits at the *end* of the file,
so it cannot be rewritten in one forward pass — it must be buffered whole. That breaks NFR-1
(no whole-file buffering) and NFR-3 (no large-object-heap allocations; anything over ~85,000
bytes lands there), and `DocumentFormat.OpenXml` is unlikely to survive trimming under NFR-9.
It is also not a text file, so it sits outside the ticket entirely.

**The part worth saying out loud:** this is exactly why `MutationCapability` exists on the
port. If every adapter presented a uniform interface, adding a buffering one would quietly
turn "zero-allocation" into "zero-allocation for `.txt`" with nothing in the type system
saying so. Declaring `BufferedRewrite` forces the admission.

**Where:** ADR-0009, PRD out-of-scope table.

### 1.5 Everything else that was deliberately not built

Persistence, audit trails, OpenTelemetry, authentication, rate limiting, queue ingestion,
blob storage, file query/delete, GDPR/PII handling, BDD tooling, index tuning.

**Why, in one line:** each was traced back to a requirement the ticket does not state. The
`IFileRepository` port exists so persistence is a later swap rather than a rewrite — that is
the difference between deferring a decision and ignoring it.

**Where:** PRD out-of-scope table (14 rows, each with its reason), PRD open questions OQ-1…4.

---

## 2. Architecture

### 2.1 Native AOT

**Asked:** is AOT compilation possible here?

**Decided:** yes, and worth it — but proven by publishing, not by discussion. Task 000 is a
one-hour timeboxed spike that settles it before anything is built on top.

**Why for:** no JIT warm-up, so first-request latency is predictable exactly when load
arrives; lower resident memory per instance, which matters because this is a shared capability
that scales horizontally; fast cold start makes scale-to-zero viable. And the reason that
actually decided it: **AOT demands reflection-free, trim-safe code, and so does a
zero-allocation hot path.** The two constraints reinforce each other rather than competing,
and the analyzers catch violations at build time.

**Costs accepted:** per-RID publish output, no reflection-based serialisation, rules out
Swashbuckle and Newtonsoft.Json, slower publish, worse debugging story for the artifact.

**Escape hatch:** the ticket explicitly requires a Swagger-style UI. If AOT and a working
OpenAPI UI conflict in practice, the UI wins and AOT is dropped — with the reason recorded.

**Sequenced first, deliberately.** The spike sits in its own task ahead of the skeleton
rather than inside it. The skeleton blocks every other task, so leaving an unresolved
feasibility question in it would put the riskiest unknown on the critical path — and the
answer changes the package stack everything else builds against. One hour, and "no" is a
perfectly good outcome.

**Where:** epic "Why Native AOT", PRD NFR-9, task 000.

### 2.2 Swagger → Scalar

**Asked (implicitly):** the ticket says "Swagger UI-enabled endpoint" — why isn't it Swagger?

**Decided:** built-in `Microsoft.AspNetCore.OpenApi` for the document, `Scalar.AspNetCore` for
the browser UI.

**Why:** Swashbuckle is reflection-based and blocks AOT. Scalar serves a static shell that
fetches the OpenAPI document, so the requirement — a browser UI you can upload through — is
met without the reflection dependency.

**Worth flagging in the review:** this is a literal deviation from a word in the ticket. It is
defensible, but it should be presented as a decision that was noticed and made, not as an
oversight.

**Where:** epic architecture decisions; ADR-0006 planned in task 010.

### 2.3 No aggregate root

**Asked (self-imposed):** the ticket asks for DDD — where is the aggregate?

**Decided:** there isn't one. `FileName` is a value object; that is the whole model.

**Why:** the operation is a stateless transformation with no identity, no lifecycle, and no
invariant spanning entities. Inventing an `UploadedFile` aggregate to look DDD-shaped is cargo
cult. **Knowing when not to apply a pattern is the DDD competence being demonstrated**, and
that is a better answer than a ceremonial aggregate would have been.

**Where:** epic "Domain Modelling Stance", `.claude/rules/dotnet-conventions.md`.

### 2.4 Policy vs plumbing

**Decided:** Domain owns *what* is appended, as a pure function over `Span<byte>`.
Infrastructure owns the `System.IO.Pipelines` mechanics that feed it.

**Why:** this keeps Domain I/O-free, as Clean Architecture requires, *and* makes the hot path
unit-testable against a stack-allocated span with no streams at all. The architectural goal
and the testability goal happen to want the same split — that coincidence is the reason to
trust it.

**Where:** epic, tasks 002 and 003.

### 2.5 Format identity on the existing port

**Asked:** should there be a separate `IFileFormatMutator`?

**Decided:** no. `IFileMutator` gains `Format` and `MutationCapability`; a registry resolves
one adapter and returns a result — never a fallback — when none matches.

**Why:** a parallel port would have taken the same inputs and returned the same outputs,
differing from the existing one in name only. That is duplication, not abstraction.

**Why no fallback:** a default mutator would mean an unmatched format silently gets treated as
plain text — corrupting a file the endpoint promised to reject. Rejection by construction
beats rejection by remembering to check.

**Where:** ADR-0009, task 002.

### 2.6 CQRS / MediatR

**Asked by the ticket:** "Explore CQRS patterns (e.g. MediatR) if applicable."

**Decided:** evaluate three options (MediatR, a hand-rolled dispatcher, Wolverine), record the
comparison in an ADR, ship the simple service on `main`, and build the winner on a branch as a
counterfactual a reviewer can diff against.

**Why:** one command, no divergent read model — mediator indirection buys nothing here. Note
the ticket says *explore* and *if applicable*: an ADR concluding "evaluated, not adopted"
satisfies it. **Deciding is mandatory; adopting is not.** Building both is what turns that
from an assertion into evidence.

**Licensing, because it is a real procurement fact and not a footnote:** MediatR has been
commercially licensed since v13 (July 2025), with a Community tier free under $5M revenue.

**Where:** tasks 009 and 013; ADR-0002 planned in task 009.

### 2.7 Native seams over invented ones

**Decided:** `TimeProvider` (.NET 8+) rather than a custom `IClock`; `Guid.CreateVersion7()`
(.NET 9+) rather than a sequential-GUID helper; built-in `AddProblemDetails()` rather than a
custom error envelope; `Results.File(..., fileDownloadName:)` rather than hand-written
`Content-Disposition` (it handles RFC 5987 encoding for free).

**Why:** every one of these is a seam the framework already provides. Writing our own would
add code, add a thing to test, and signal unfamiliarity with the platform.

**Where:** `.claude/rules/dotnet-conventions.md` non-negotiables table.

### 2.8 Ports with one implementation

**Decided:** that is fine and not a smell.

**Why:** `IFileMutator`, `IFileRepository` and `IRandomSequenceGenerator` each have one
adapter. A port exists to invert a dependency and keep the core testable, so it is judged by
whether the dependency needs inverting — not by how many adapters exist. "No interface with
one implementation" is aimed at speculative generality in application code, not at
ports-and-adapters seams, which is the architecture the ticket asked for.

**Where:** `.claude/rules/dotnet-conventions.md`.

---

## 3. Verification

### 3.1 Branch coverage, not line coverage

**Decided:** ≥80% **branch** coverage, gated in CI.

**Why:** line coverage is easy to pass without exercising conditionals — a test can execute
every line of an `if` chain while only ever taking one path. Branch coverage is the number
that means what people think line coverage means. 80% is the industry convention; naming
*branch* is the part that shows the convention is understood rather than repeated.

### 3.2 Architecture tests

**Decided:** a small ArchUnitNET ruleset (3–6 rules) enforcing the dependency rule.

**Why:** the ticket asks for DDD and Clean Code. "We adhere to clean layering" in a README is
an assertion; a failing build is evidence. Deliberately small — enough to catch real
violations, not a fitness-function framework.

### 3.3 Benchmarks are not tests

**Decided:** BenchmarkDotNet with `[MemoryDiagnoser]` in its own project, excluded from
`dotnet test` and from the coverage gate.

**Why:** coverage proves the code is exercised and says nothing about allocation. They answer
different questions and have different requirements — benchmarks need Release configuration
and a quiet machine. Folding them into the gated suite would make both worse.

### 3.4 The coverage gate ships before the code it gates

**Decided:** scaffold the gate at threshold 0 in task 001; task 012 ratchets it to 80%.

**Why:** a gate added halfway through means every PR before it merged ungated. Threshold 0
from the first merge means the mechanism is proven early and only the number changes later.

**Where:** tasks 001 and 012, `.claude/rules/definition-of-ready-done.md`.

---

## 4. Things that changed when checked

The most useful section for a conversation about approach: these are the points where the
first answer was wrong and checking changed it.

| Question | First answer | What checking showed |
|---|---|---|
| Is AOT compatible with JSON handling? | Overstated as broadly incompatible | Reflection-based serialisation breaks under trimming, but the `System.Text.Json` **source generator** is fine. AOT stayed, Newtonsoft was excluded — a narrower and correct constraint |
| Is an interface with one implementation forbidden here? | Asserted yes, and written into the rules | No such rule existed in this repo — it was imported from a general heuristic and wrongly presented as a project convention. Removed. The real finding underneath was better: the proposed second port duplicated an existing one |
| Should the extra tooling (skills, ArchUnit, coverage) be cut as over-engineering? | Recommended cutting | Wrong once the goal was clear: the deliverable is evidence of practice, not the smallest solution. The brief says the reviewer cares about approach over a perfect solution |

---

## 5. Still open

| # | Question | Current default |
|---|---|---|
| OQ-1 | Does the mutated file need to be persisted and retrievable later? | Synchronous return only; the port exists so persistence is a swap, not a rewrite |
| OQ-2 | Does a regulated (medical/ISO) context apply, requiring an immutable audit trail? | Out of scope; nothing in the ticket implies a regulated domain |
| OQ-3 | Expected concurrency and volume? | "Tens concurrent" assumed; design targets no unbounded per-request allocation |
| OQ-4 | Auth model for the endpoint? | Open for the exercise; production posture (Entra ID/JWT) documented, not implemented |

OQ-1 and OQ-2 are the two worth raising with the product owner — they gate the largest amount
of potential scope, and they are the reason task 005 is deferred rather than guessed at.

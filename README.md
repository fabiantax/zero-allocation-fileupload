# File Mutation API

A .NET 10 REST API: upload a text file, get it back with the current UTC date and a random
character sequence appended, downloaded under its original filename. Built for a backend trial
assignment.

## Status

Planning complete; implementation starting. Nothing under `src/` yet — the first code lands with
[#3](../../issues/3), behind a one-hour feasibility spike ([#2](../../issues/2)) that decides
whether Native AOT survives alongside a browser OpenAPI UI.

## Why there is more here than a couple of hours of code

The brief asks for a couple of hours' work and says the reviewer is *"more interested in your
problem-solving approach and coding style than in a perfect solution."* This repository is built
to answer that sentence directly. The feature is small on purpose; what surrounds it is the
actual submission.

That is a deliberate trade, and it has a real cost — an independent review of these documents
said the planning "misses the assignment's explicit time constraint by roughly an order of
magnitude." That criticism is fair on its face, so here is the reasoning rather than a defence:

- **The feature cannot differentiate anything.** Appending a date to a file is thirty lines.
  Two candidates will submit the same thirty lines. What differs is what they chose *not* to
  build, and whether their claims are checkable.
- **The claims are mechanically verified, not asserted.** Clean layering is enforced by
  ArchUnitNET, allocation behaviour is measured by BenchmarkDotNet, branch coverage is gated in
  CI. A README saying "adheres to Clean Architecture" is worth nothing; a failing build is
  worth something.
- **The reasoning is written down as it happened**, including the parts that turned out wrong.
  See [`docs/decision-log.md`](docs/decision-log.md), particularly *"Things that changed when
  checked."*
- **It is how the work was actually run.** The planning artifacts are agent-executable task
  specifications, and the process is the demonstration.

If the proportion is wrong for your taste, that judgement is a legitimate outcome of the
exercise, and the reasoning above is what to argue with.

## Where to look

| If you want to see | Read |
|---|---|
| Why each design decision was made | [`docs/decision-log.md`](docs/decision-log.md) |
| The system in three diagrams | [`docs/architecture.md`](docs/architecture.md) |
| Decisions in full, with alternatives | [`docs/adr/`](docs/adr/) |
| What was deliberately **not** built | [PRD out-of-scope table](.claude/prds/file-mutation-api.md) |
| The work breakdown | [Epic #1](../../issues/1) and its 13 sub-issues |

## Known open issues

Honest list, rather than a clean surface:

1. **Late validation cannot return 415.** The design streams mutated bytes while still
   validating UTF-8, then promises a `ProblemDetails` on failure — impossible once the response
   has started. Three resolutions are documented at the top of
   [`docs/architecture.md`](docs/architecture.md); none is chosen yet.
2. **Native AOT is proven but parked.** [#2](../../issues/2) published a native binary that
   served the OpenAPI document and Scalar UI with zero trim warnings (13.15 MiB, 17.6 ms median
   cold start). It is deliberately **not enabled**: setting `PublishAot` runs the analyzers on
   every build and cost a five-minute cold build, and a fast inner loop is worth more than a
   startup optimisation nobody asked for. See `docs/adr/0001-native-aot.md`.
3. **OQ-2 (audit trail / regulated context) is unanswered.** Default taken: out of scope.

## Scope

Accepted: `.txt`, declared `text/plain`, decodable as UTF-8. Everything else is rejected with
415 — including files that decode perfectly well, such as `.json` and `.csv`, because appending
a date to them produces structurally invalid output that still looks like success.

Nothing is persisted. Upload, mutate, return; the bytes go to the response and are then gone.

## Licence

MIT — see [LICENSE](LICENSE).

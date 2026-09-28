---
name: verification-depth
status: backlog
created: 2026-09-28T10:00:00Z
updated: 2026-09-28T10:00:00Z
progress: 0%
prd: .claude/prds/file-mutation-api.md
github: https://github.com/fabiantax/zero-allocation-fileupload/issues/101
milestone: v0.2 — Verification depth
---

# Epic: verification-depth

## Overview

v0.1 proves the code *runs*: branch coverage is gated at 80%, architecture rules pass, and
allocation is measured. It does not yet prove the tests would *notice a wrong
implementation*, it does not show the acceptance criteria in a form the product owner can
read, and its example tests sample the input space rather than cover it.

This epic adds three verification layers, each chosen for a defect class the current suite
cannot detect:

| Layer | Tool | The question it answers that v0.1 cannot |
|---|---|---|
| Property-based tests | CsCheck (proposed, settled in ADR-0010) | Does the invariant hold for *every* input and every segment split, not just the five examples someone thought of? |
| Executable acceptance criteria | Gherkin via Reqnroll (proposed, settled in ADR-0010) | Can the product owner read what the API promises, and is each PRD acceptance criterion demonstrably executed? |
| Mutation testing | Stryker.NET 5.0.0 | If the implementation were subtly wrong, would any test fail? Branch coverage measures executed branches, not assertions that detect a change |

## Why this reverses a recorded decision

The PRD's out-of-scope table excluded Gherkin and Stryker in v0.1, and
`.claude/rules/dotnet-conventions.md` lists both under "do not build". Those were correct for a
time-boxed trial. The product owner has now moved them into scope. Reversing a written decision
silently is the failure this repo exists to avoid, so task 000 records the reversal as ADR-0010
**before** any tool is added, and every later task depends on it.

## Why test-only stories do not break the story-splitting rule

`.claude/rules/story-splitting.md` says "Tests ship with their implementation" and forbids a
story whose only content is testing another story's work. The rule closes two failures: a test
story that depends on unmerged implementation stories (turning parallel streams serial), and an
implementation that claims done without proof. Neither applies here. The implementation merged
in v0.1 with its tests; these stories add new verification capability over merged code and
depend on no open implementation story.

## Waves and runnable count

| Phase | Stories | Runnable when phase starts |
|---|---|---|
| 1 | 000 (ADR-0010), plus the independent README benchmark-claim bug | 2 |
| 2 | 001 (properties), 002 (Gherkin), 003 (Stryker) | 3 |
| 3 | 004 (README surfaces the new layers) | 1 |

Series `2, 3, 1`. Phase 3 is a deliberate fan-in: it is a markdown-only story that reports the
measured results of phase 2, so it cannot usefully run earlier.

## File ownership (phase 2 runs in parallel, so no overlap)

| Story | Owns |
|---|---|
| 001 | `tests/FileMutation.{Domain,Application,Infrastructure}.Tests/*.csproj` and new `*Properties.cs` files |
| 002 | new `tests/FileMutation.Acceptance.Tests/`, `FileMutation.sln`, `tests/FileMutation.Api.Tests/FileMutationApiFactory.cs`, `tests/FileMutation.TestCommon/` |
| 003 | `.config/dotnet-tools.json`, `stryker-config.json`, `.github/workflows/mutation.yml`, `docs/quality/mutation-results.md`, `.gitignore` |

`README.md` is owned by the bug fix in phase 1 and by 004 in phase 3, never concurrently.

## Success Criteria (Technical)

- [ ] ADR-0010 merged before any new test package is referenced
- [ ] Each property is shown to fail against a planted bug before it is trusted
- [ ] Every US-1 and US-2 acceptance criterion maps to one scenario, or is listed as not expressible over HTTP with a reason
- [ ] Baseline mutation score per project recorded with the exact command; every surviving mutant triaged
- [ ] The mutation gate is shown to fail on purpose before it is trusted
- [ ] Branch coverage not lower than before any story in this epic
- [ ] No PR exceeds ~400 LOC of code

## Estimated Effort

| Phase | Tasks | Estimate |
|---|---|---|
| 1 | 000 | ~1 h |
| 2 | 001, 002, 003 | ~5.5 h (parallelisable) |
| 3 | 004 | ~0.25 h |

## Tasks Created
- [ ] 000.md - ADR-0010: bring property-based, Gherkin and mutation testing into scope (parallel: false)
- [ ] 001.md - Property-based tests for the mutation and acceptance invariants (parallel: true)
- [ ] 002.md - Gherkin acceptance scenarios for US-1 and US-2 (parallel: true)
- [ ] 003.md - Stryker.NET mutation testing with a ratcheting CI gate (parallel: true)
- [ ] 004.md - README: surface the property, acceptance and mutation layers (parallel: false)

Total tasks: 5
Parallel tasks: 3
Sequential tasks: 2
Estimated total effort: ~6.75 hours

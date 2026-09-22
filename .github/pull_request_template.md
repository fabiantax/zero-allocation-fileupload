<!--
PR title must contain the issue number, e.g. "Add upload endpoint (#42)".
CI enforces this — see .github/workflows/pr-title-check.yml
-->

Closes #

## What changed

<!-- One or two sentences. The diff shows what; say why. -->

## Definition of Done

Machine-checked in CI — tick only what you have actually confirmed:

- [ ] Every acceptance criterion on the issue is met
- [ ] `dotnet build` clean, including trim/AOT analyzer warnings
- [ ] Tests pass, and **would fail if this change were reverted**
- [ ] Branch coverage ≥80% and not lower than before
- [ ] Architecture tests pass
- [ ] XML documentation on new public members

Code review — **run before requesting a human reviewer**:

- [ ] `/code-review` run on this diff
- [ ] `/security-review` run on this diff
- [ ] `dotnet-code-reviewer` run (allocation/AOT, Clean Architecture, PR hygiene)
- [ ] `/codex:review --background` run — independent model, different blind spots
- [ ] Findings fixed, or listed below with why they stand

<!-- Findings you consciously did not fix, and why: -->

Delivery:

- [ ] ≤~400 LOC **of code** (markdown excluded), scoped to this story — no drive-by refactors
- [ ] ADR added or updated if this embodies a decision a future reader would question
- [ ] No credentials, internal hostnames, or client-identifying content

## Decisions taken

<!--
Anything you chose that a reviewer might have chosen differently, and why.
If it is significant, it belongs in docs/adr/ rather than here.
-->

## How to verify

<!-- The command or steps a reviewer runs to see it working. -->

```bash

```

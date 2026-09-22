# Definition of Ready / Definition of Done

Two gates. A story that fails DoR is not started; a story that fails DoD is not merged.

## Definition of Ready

A story may be picked up only when all of these hold:

- [ ] **Acceptance criteria are written and testable.** Each one states an observable
      outcome — something a test or a command can confirm. "Works well" is not a criterion.
- [ ] **It fits in one PR** (~400 changed LOC of code; markdown and generated files are
      excluded — see `.claude/rules/github-workflow.md`).
      If it plainly does not, it is split *before* it is started, not during.
- [ ] **Dependencies are resolved or explicitly non-blocking.** If it depends on another
      story, that story is merged, or the interface it needs already exists.
- [ ] **No open question would change the approach.** An unanswered question with an agreed
      default is fine — the default is written down. An unanswered question that would cause
      rework is not.
- [ ] **The proof is known.** Before starting, we can name which test, measurement, or
      check will demonstrate it works. If nothing can demonstrate it, the story is not ready.
- [ ] **A pre-mortem exists.** Assume the story failed — why? One likely failure, one early
      signal that would reveal it, one kill criterion. Written before starting; written
      afterwards it is a post-mortem and has already cost what it was meant to save.
      Tooling and environment count, and usually dominate here.
- [ ] **It is linked** to the epic and has a GitHub issue number.

## Definition of Done

A story is done only when all of these hold:

**Function**
- [ ] Every acceptance criterion is met and demonstrated
- [ ] Behaviour verified end to end, not only in unit tests

**Quality gates (all enforced by CI, none self-reported)**
- [ ] `dotnet build` clean — no warnings, including trim/AOT analyzer warnings
- [ ] All tests pass
- [ ] Branch coverage ≥80% and not lower than before the change
- [ ] Architecture tests pass
- [ ] Tests would actually fail if the change were reverted

**Code**
- [ ] XML documentation on new public types and members
- [ ] No business rule duplicated across layers
- [ ] No new client-identifying content (`hooks/pre-commit` enforces this)

**Code review — runs BEFORE the PR review is requested**

Never hand a reviewer something a machine would have caught. Run these on the diff, fix what
they find, *then* request human review:

- [ ] `/code-review` — generic correctness bugs
- [ ] `/security-review` — vulnerabilities
- [ ] `dotnet-code-reviewer` agent — this project's standards: hot-path allocation and
      AOT/trim safety, Clean Architecture violations architecture tests cannot catch, PR
      hygiene. (From the team marketplace: `/plugin install code-review@<marketplace>`.)
- [ ] `/codex:review --background` — an **independent model** on the same diff
- [ ] Every finding is either fixed, or answered in the PR description saying why it stands

The Codex pass is not redundant with the others, and it is the one worth keeping if you drop
any. The first three are Claude reviewing Claude's own output, which is structurally blind to
the errors Claude makes systematically — a different model has different blind spots, so it
catches a class the others cannot. Run it with `--background` so it works on Codex's budget
while you keep going, and `--base main` for a whole-branch review.

A PR opened without this is sending a human to do a machine's job, and it burns the one
reviewer pass you get before people stop reading carefully.

**Delivery**
- [ ] PR ≤~400 LOC of code and scoped to this story — no drive-by refactors
- [ ] PR title references the issue (`#N`); CI enforces it
- [ ] Reviewed and approved by someone other than the author
- [ ] An ADR exists for any decision a future reader would question
- [ ] The merged PR closes the issue (`Closes #N`)

## Why these two gates

**Problem**: work starts on vague stories, then stalls mid-flight while someone chases a
missing answer — and the cost is paid with a half-finished branch open.
**Rule**: DoR is checked before starting, not during.

**Problem**: "done" quietly means "the code is written", and the verification debt surfaces
weeks later.
**Rule**: every DoD quality gate is machine-checked in CI. If a gate can only be confirmed
by someone asserting it, it is not a gate.

**Problem**: the human reviewer becomes the first line of defence, spending their attention
on allocation slips and layering violations a tool finds in seconds — and by the time they
reach the design questions only they can answer, they have stopped reading carefully.
**Rule**: automated review runs *before* the review request, not after. Note honestly that
this one is **self-attested** — the checkbox proves someone claimed to run it, not that they
did. Making it a real gate means a CI job running the review and posting findings on the PR;
until that exists, treat the box as an honour-system item, not evidence.

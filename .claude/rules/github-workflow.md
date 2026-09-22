# GitHub Workflow Rules

Problems this repo's workflow must not fall into, and the rule that closes each one.

## PR size
**Problem**: large PRs are unreviewable, hide bugs, and rot on the branch waiting for review.
**Rule**: keep PRs under ~400 LOC of **code** changed. If a change is bigger, split it into
stacked/sequential PRs before opening.

**Counts toward the cap** — anything a reviewer must reason about line by line, and anything
that executes: `.cs`, `.csproj`/`.props`/`.sln`, CI workflows, `appsettings.*`, shell scripts,
git hooks. Tests count; a large test file is still a large thing to review.

**Does not count**: markdown (docs, ADRs, PRDs, epic and task files), issue and PR templates,
`.gitignore`, LICENSE, generated files, lockfiles.

**Why the split**: the cap exists because review attention degrades with diff size, and it
degrades differently for the two. Code review is non-linear — a reviewer has to hold
interactions between distant lines in their head, so 800 lines is far worse than twice 400.
Prose is read linearly and can be skimmed for structure, so a long document is tiring, not
unreviewable.

**The loophole this leaves, and its guard**: a PR could now carry unlimited markdown. Docs are
exempt from the *cap*, not from the *scope* rule below — a PR still does one thing. Bundling
unrelated documents to dodge review is the abuse; if a doc PR is so large nobody reads it, the
problem is that it should have been several documents.

## Branch protection
**Problem**: an unprotected `main` lets a bad commit or force-push land with no check.
**Rule**: `main` has a GitHub branch-protection ruleset — no force-push, no direct push, required status checks, at least one review before merge.

## PR-only changes
**Problem**: direct commits to `main` bypass review and CI entirely.
**Rule**: all changes to `main` land via PR. Branch protection enforces this; never `git push origin main` directly, never merge with admin override.

## CI signal
**Problem**: without CI, a broken build or failing test is only caught after merge (or never).
**Rule**: a minimal GitHub Actions workflow (`.github/workflows/ci.yml`) builds and runs tests on every push and PR to `main`.

## Pre-merge gate
**Problem**: a PR can be merged while the build is broken or tests are red.
**Rule**: the CI workflow is a required status check — merge is blocked unless **the solution builds and all tests succeed**.

## User story hygiene
**Problem**: vague issues ("make uploads better") can't be estimated, sized, or verified done.
**Rule**: every user story issue has acceptance criteria, is linked to a milestone, and is sized to fit in one PR (see PR size rule above). No story ships without a closing PR that references it (`Closes #N`).

## Issue traceability
**Problem**: a commit/PR with no issue reference can't be traced back to a user story.
**Rule**: two layers. Locally, `hooks/commit-msg` rejects a commit whose message has no `#<number>` (activate once per clone: `git config core.hooksPath hooks`) — fast feedback, but opt-in like any git hook. The real gate is server-side: `.github/workflows/pr-title-check.yml` is a required status check that fails any PR whose title lacks `#<issue-number>` — applies to every contributor with no setup, can't be bypassed with `--no-verify`.

## Commits
**Problem**: unreviewable commit history makes `git bisect`/rollback slow.
**Rule**: atomic commits (one logical change each), present-tense summary line ≤72 chars.

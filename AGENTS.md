# Agent instructions

These apply to any agent or tool working in this repository. `CLAUDE.md` carries the same
rules for Claude Code; read whichever your tooling loads.

## What this repo is

A .NET 10 REST API built as a technical trial assignment for a client. A caller uploads a
text file through an OpenAPI UI; the API appends the current UTC date and a random character
sequence and returns the mutated file for download under its original filename. That is the
entire functional requirement.

**The feature is not the point.** The brief asks for a couple of hours' work and states the
reviewer cares more about problem-solving approach and coding style than a perfect solution.
So the deliverable is evidence of judgment: what was deliberately *not* built and why
(see the PRD's out-of-scope table), claims verified rather than asserted (ArchUnitNET enforces
layering, BenchmarkDotNet measures allocation, CI gates branch coverage), decisions recorded
in ADRs as they were made, and the agent-assisted practice used to run the work.

The upload path streams via `System.IO.Pipelines` with no whole-file buffering and publishes
with Native AOT — that is where the technical depth is shown.

**Stack:** .NET 10, minimal APIs, Native AOT, built-in `Microsoft.AspNetCore.OpenApi` +
`Scalar.AspNetCore`, `ProblemDetails`, `System.Text.Json` source generator, `TimeProvider`,
built-in DI. Tests: xUnit, `WebApplicationFactory`, ArchUnitNET, coverlet + ReportGenerator
(branch coverage), BenchmarkDotNet. CI on GitHub Actions.

The pattern is **built-in before third-party** — every added package is one that must be
trim-safe for AOT. Excluded on purpose: Swashbuckle and Newtonsoft.Json (reflection-based,
block AOT), MVC controllers (not AOT-supported), EF Core/SQLite (no persistence requirement).

**Before simplifying:** the planning weight is deliberate and is part of the deliverable. Do
not strip it as cleanup. Every rule in `.claude/rules/` names the failure it closes; if one
does not, raise it rather than silently dropping it.

Work lives in `.claude/prds/`, `.claude/epics/file-mutation-api/` (14 self-sufficient task
files) and `docs/adr/`.

## Confidentiality: no client identifiers

This is a **public repository**. Never write the client's or company's name — or any other
identifying detail (people, internal system names, customer names, internal URLs) — into
anything in this repo. That includes:

- code, namespaces, project names and folder names
- comments and XML documentation
- markdown docs, ADRs, PRDs, task files
- commit messages, branch names, PR titles and descriptions
- test fixtures and sample data

The specific terms to avoid are listed in `CLAUDE.local.md`, which is gitignored and never
leaves the machine. **If that file is missing, ask before writing anything that could
identify the client** rather than guessing.

Use neutral wording instead: "the client", "the company", "the product owner".

This applies to derived content too. When transcribing or summarising a client-supplied
document, strip the identifiers as you go — do not copy them in and plan to clean up later.

A `hooks/pre-commit` hook enforces this mechanically. Activate it once per clone:

```bash
git config core.hooksPath hooks
```

## Autonomy

Default to acting, then report in one line. Do not ask permission for mechanical steps —
committing, pushing, opening a PR, or **merging a PR whose CI is green and whose DoD is
satisfied**.

Stop and ask only for: force-push or rewritten history; deleting an issue, milestone, repo or
unmerged branch; changing branch protection or repo visibility; publishing outside this repo;
licence changes or commercial/copyleft dependencies; spending money; writing a client
identifier; merging with CI red; settling a PRD open question.

When unclear, prefer the reversible action and report it rather than asking. Full list in
`CLAUDE.md`.

## Workflow rules

See `.claude/rules/github-workflow.md` for PR size, branch protection, issue traceability
and CI gates.

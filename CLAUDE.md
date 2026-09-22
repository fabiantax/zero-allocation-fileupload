# Project instructions

## What this repo is

A .NET 10 REST API built as a technical trial assignment for a client. The functional
requirement is small and fixed: a caller uploads a text file through an OpenAPI UI, the API
appends the current UTC date and a random character sequence to its content, and returns the
mutated file for download under its original filename. That is all of it.

**The feature is not the point.** The brief says it should take a couple of hours and that
the reviewer is "more interested in your problem-solving approach and coding style than in a
perfect solution". What is actually being delivered is evidence of engineering judgment:

- **What we deliberately did not build**, and why. The PRD's out-of-scope table is a
  first-class deliverable — persistence, audit trails, OpenTelemetry, auth, rate limiting and
  queueing were each considered and declined for a stated reason. Restraint is the signal.
- **Claims that are verified rather than asserted.** Clean Architecture is enforced by
  ArchUnitNET, not claimed in a README. Allocation behaviour is measured by BenchmarkDotNet,
  not asserted. Coverage is gated in CI on branch, not reported.
- **Decisions recorded as they were made.** ADRs capture the alternatives and the reasoning,
  including for choices that were rejected.
- **How the work is run with AI agents** — planning, decomposition, delegation and
  verification. That practice is part of what is being shown, not overhead around it.

The repo name reflects the technical throughline: the upload path streams via
`System.IO.Pipelines` with no whole-file buffering, and publishes with Native AOT. Both are
deliberate — they are where the depth is demonstrated, and they reinforce each other, since
AOT's trim-safe discipline and a zero-allocation hot path demand the same thing.

### Tech stack

| Area | Choice |
|---|---|
| Runtime | .NET 10 (LTS), C# |
| API | Minimal APIs — MVC controllers are not AOT-supported |
| Publish | Native AOT (`PublishAot`), per task 000's spike outcome |
| OpenAPI doc | Built-in `Microsoft.AspNetCore.OpenApi` |
| Browser UI | `Scalar.AspNetCore` |
| Errors | Built-in `AddProblemDetails()` — RFC 7807 |
| JSON | `System.Text.Json` **source generator** (`JsonSerializerContext`) |
| Streaming | `System.IO.Pipelines`, `ReadOnlySequence<byte>`, `Span<byte>`/`Memory<byte>` |
| Time | `TimeProvider` (BCL, .NET 8+) |
| DI | Built-in container, `ValidateOnBuild` + `ValidateScopes` |
| Unit tests | xUnit |
| Integration tests | `WebApplicationFactory` |
| Architecture tests | ArchUnitNET |
| Coverage | `coverlet.collector` → `ReportGenerator`, gated on **branch** |
| Allocation proof | BenchmarkDotNet + `[MemoryDiagnoser]` |
| CI | GitHub Actions |
| CQRS variant only | MediatR / hand-rolled dispatcher / Wolverine — task 009 decides |

**The pattern: built-in before third-party.** `TimeProvider`, `ProblemDetails`, OpenAPI, the
DI container and `Guid.CreateVersion7()` are all BCL or framework rather than packages. That
keeps the dependency surface small, and it is also what makes AOT viable — every package
added is a package that has to be trim-safe.

**Excluded on purpose** — do not reach for these:

| Not used | Why |
|---|---|
| Swashbuckle | Reflection-based; blocks Native AOT |
| Newtonsoft.Json | Reflection-based; cannot be made AOT-safe |
| MVC controllers | Not AOT-supported |
| EF Core / SQLite | No persistence requirement (PRD OQ-1) |

### Why there is this much process around a small feature

An agent reading this repo may reasonably conclude the planning weight is disproportionate to
a two-hour ticket. Understand before simplifying: the process *is* part of the deliverable
here, and it was built deliberately. Do not strip it to "clean up".

The one thing that must not happen is process without substance. Every rule in
`.claude/rules/` exists because it closes a specific failure, and each says which. If you
find one that does not, that is worth raising — but raise it, do not silently drop it.

### Where the work lives

- `.claude/prds/file-mutation-api.md` — requirements, user stories US-1/2/3, out-of-scope
  table, open questions
- `.claude/epics/file-mutation-api/` — the epic plus 14 task files, each self-sufficient for
  an agent to pick up cold
- `docs/adr/` — decisions and their reasoning

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

## Working rules

Full detail lives in `.claude/rules/` (all of it loads automatically — the tables below are
the operative summary, not a second copy of the reasoning):

| File | Covers |
|---|---|
| `github-workflow.md` | PR size, branch protection, issue traceability, CI gates |
| `definition-of-ready-done.md` | DoR before starting, DoD before merging, pre-PR code review |
| `dotnet-conventions.md` | Solution layout, non-negotiables, what not to build |
| `complexity-routing.md` | Complexity scale and model routing |
| `work-classification.md` | `work_type` and WBSO candidate flagging |

## Model routing

**Sonnet is the default.** Every story carries `complexity: 1-5`, describing what the work
demands — reasoning difficulty, **not** size and **not** risk.

| Cx | Means | Picks up the story | Escalate to |
|---|---|---|---|
| 1 | Mechanical, one correct answer | Sonnet (medium) | Sonnet (max) |
| 2 | Routine, well-trodden pattern | Sonnet (medium) | Sonnet (max) |
| 3 | Problem-solving inside a known design | Sonnet (max) | Opus (medium) |
| 4 | Design leverage, expensive to reverse | Opus (medium) | Opus (max) |
| 5 | Subtle failure modes that pass a casual read | Opus (max) | Stop, ask a human |

**Haiku never picks up a story.** It is a delegation target — what Sonnet or Opus fans out to
after reading the problem and carving off a piece that needs no judgment: short,
self-contained, one correct answer, and cheaply verifiable by the orchestrator. If the piece
requires deciding *what* to do rather than doing a decided thing, it is not Haiku work.

Escalate on evidence — two failed review/test attempts, or the model saying it cannot
determine the answer — not because a task felt hard. The orchestrator stays responsible for
delegated work: verify what comes back, since a small model reporting success is exactly
where a silent wrong answer enters.

**Fable is not used here.** It is reserved for major architectural decisions and genuinely
novel algorithms. If a task here seems to need it, that is a signal about the *story* — too
broad, or hiding an unstated decision — not about the model.

**Risk is a separate axis.** A find-and-replace across a public repo is complexity 1 and high
blast radius. Route on complexity; *gate* on risk — anything touching published artifacts,
credentials, git history or CI gets a human check regardless of its number.

## Work classification

Every story carries two accounting fields:

- **`work_type: new | maintenance`** — this epic is greenfield, so everything is `new`.
- **`wbso_candidate: true | false`** — a *flag, not a determination*. Mark it when there was
  genuine technical uncertainty **at the point the work started**, resolved by systematic
  investigation. Difficulty is not the criterion; uncertainty is — a complexity-5 task using
  an established technique is not a candidate. Whoever handles the application decides; an
  engineer's checkbox is not an eligibility ruling.

Spikes and ADRs are the evidence trail. Write them as you go — reconstructed after the fact
they are worth little.

---

# Orchestration

How agent work is organised in this repo, and the facts that actually change a decision.
Verified against the Claude Code docs on 2026-09-22; behaviour marked *(version-dependent)*
moves quickly, so confirm before relying on it.

## Where configuration lives, and what wins

Instruction files are **concatenated, not overridden** — a narrower file adds to the broader
one rather than replacing it. Order, broadest to narrowest:

```
managed policy  →  ~/.claude/CLAUDE.md  →  ./CLAUDE.md  →  ./CLAUDE.local.md
```

- `@path` imports pull in other files, resolving relative to the **importing file**, up to
  4 hops deep.
- **`AGENTS.md` is only read when no `CLAUDE.md` exists** in cwd or above *(version-dependent)*.
  So in this repo Claude Code reads `CLAUDE.md`; `AGENTS.md` exists for other agent tooling.
  That is why the confidentiality rule is duplicated into both rather than cross-referenced —
  a pointer would be invisible to whichever tool reads only one.
- `.claude/rules/*.md` load automatically. Add `paths:` glob frontmatter to a rule and it
  loads **only when Claude touches a matching file** — use that for rules that would
  otherwise be dead weight in every conversation.

Settings precedence (highest first): managed → `--settings` → `.claude/settings.local.json`
→ `.claude/settings.json` → `~/.claude/settings.json`. List keys like `permissions.allow`
**merge** across scopes. Note `defaultMode: auto` and `bypassPermissions` are ignored from
project/local files — they must come from user or managed settings.

## CLAUDE.md vs rules vs skills vs agents

Putting a thing in the wrong place is the most common waste — context you pay for on every
turn, for guidance needed once a week.

| Use | For | Cost |
|---|---|---|
| `CLAUDE.md` | Facts true for every task here | In context always |
| `.claude/rules/*.md` | Standing rules; `paths:`-scoped ones load on demand | Always, or on match |
| Skill (`SKILL.md`) | A procedure invoked when relevant | Only the description (~1.5k char cap) until invoked |
| Subagent (`agents/*.md`) | Work that should run in its own context window | Nothing until spawned |

Skills use **progressive disclosure**: only the `description`/`when_to_use` stays resident;
the body loads on invocation, and reference files only when actually read. So a long skill
is cheap — a long `CLAUDE.md` is not.

Reusable skills and agents belong in the team marketplace repo, not here. Only put a skill
in this repo if it is meaningless outside it.

## Delegating to subagents

**Default to doing it yourself.** Delegate when the work is genuinely parallel, or when it
would flood the main context with output nobody needs to keep (wide searches, doc research,
mechanical sweeps across many files).

Rules that hold here:

- **Disjoint file sets.** Two agents editing the same file is a lost write. Partition by
  file, state the partition in each prompt, and say explicitly which files are *not* theirs.
- **One decision per agent.** A prompt carrying five sub-questions produces a treatise and
  burns tool calls. Ask for the answer plus the exact snippet.
- **Set `model` explicitly.** Sonnet for extraction, enumeration and mechanical edits; Opus
  only for the genuinely hard gating call. Resolution order is: per-invocation `model` →
  the subagent's own frontmatter (`inherit` = the main model) → `CLAUDE_CODE_SUBAGENT_MODEL`
  → the main conversation's model.
- **Verify the result.** Agents report success optimistically. A scrub, a rename or a
  migration gets an independent `grep` afterwards — trust the check, not the summary.
- **Spike before delegating.** If a build, a publish or a one-line repro settles the
  question, run it. Do not spend an agent on something a command answers.

Concurrency: **20 subagents by default** (`CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS`), nested
spawn depth 3. Foreground subagents block the conversation; background ones run alongside it
and report back when done.

Useful frontmatter: `tools` / `disallowedTools` (a reviewer gets no `Write`), `model`,
`isolation: worktree` (own git worktree, edits to the main checkout blocked), `background`,
`maxTurns`, `permissionMode`.

## Hooks — enforcement that does not depend on goodwill

An instruction in `CLAUDE.md` is advice. A hook is a gate. Anything that must not happen
gets a hook, not a paragraph.

- Configured in settings files, plugin `hooks/hooks.json`, or skill/subagent frontmatter.
- Events include `PreToolUse`, `PostToolUse`, `UserPromptSubmit`, `SessionStart`,
  `SubagentStart`/`SubagentStop`, `PreCompact`, `SessionEnd`, and ~25 more.
- Matchers are pipe-separated exact matches (`"Bash|Edit"`), or regex when the pattern needs
  it.
- The command receives event JSON on **stdin**. **Exit code 2 blocks** the action; exit 0
  lets the hook return JSON to allow/deny, inject `additionalContext`, or rewrite the tool
  input via `updatedInput`.

Note that git hooks (`hooks/`, via `core.hooksPath`) and Claude Code hooks are different
mechanisms. Enforcement that must apply to *every* contributor — not just those using Claude
Code — belongs in a git hook or CI, never in `CLAUDE.md`.

## Headless and CI

```bash
claude -p "<prompt>" --allowedTools "Read,Grep" --output-format json
claude -p "..." --agent dotnet-code-reviewer --max-budget-usd 2
```

- `--output-format json` includes `total_cost_usd`; `--json-schema` forces schema-validated
  output for anything you intend to parse.
- `--bare` skips auto-discovery of hooks, skills, commands, subagents, plugins, MCP and
  `CLAUDE.md` — use it when a CI run must be reproducible and not inherit local config.
- Chain calls by capturing `session_id` from the JSON output and passing `--resume`.
- `--max-budget-usd` caps spend including subagents; `--permission-prompts none` for
  genuinely unattended runs.

## Plan mode

For anything whose blast radius is unclear, plan first: `Shift+Tab` to cycle, or
`claude --permission-mode plan`. Reads and exploratory commands run; edits stay blocked
until the plan is approved. Cheaper than reverting a confident wrong change.

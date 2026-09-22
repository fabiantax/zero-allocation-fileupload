# Complexity and model routing

Every story carries `complexity: 1-5`. It describes what the *work* demands, so the same
number means the same thing regardless of who or what picks it up.

## The scale

| # | Means | Looks like |
|---|---|---|
| **1** | Mechanical. One correct answer, no judgment. | Renames, formatting, moving files, applying a stated convention |
| **2** | Routine. Well-trodden pattern, the shape is known before starting. | Scaffolding, a test suite for existing behaviour, wiring a documented library |
| **3** | Real problem-solving inside a known design. | Non-trivial algorithm, interpreting benchmark output, a validation path with edge cases |
| **4** | Design leverage. The decision constrains later work and is expensive to reverse. | Port and interface signatures, architecture trade-offs, build-vs-buy evaluations |
| **5** | Hard, with subtle failure modes that pass a casual read. | Concurrency, zero-allocation hot paths, multi-segment buffer handling, anything where "it works" and "it is correct" differ |

## Routing

Start at the default. Escalate only when the attempt actually stalls — not pre-emptively.

**Sonnet is the default.** No story is picked up by anything smaller.

| Complexity | Picks up the story | Escalate to |
|---|---|---|
| 1 | Sonnet (medium) | Sonnet (max) |
| 2 | Sonnet (medium) | Sonnet (max) |
| 3 | Sonnet (max) | Opus (medium) |
| 4 | Opus (medium) | Opus (max) |
| 5 | Opus (max) | Stop and ask a human |

1 and 2 route identically at story level — the difference between them is not which model
opens the story, it is how much of it can be handed off (below).

Escalating means: the model produced something that failed review or tests twice, or it said
it could not determine the answer. It does not mean the task *felt* hard.

## Haiku is a delegation target, never an entry point

Haiku does not pick up stories. It is what Sonnet or Opus fans work out to once they have
already read the problem and carved off a piece that needs no judgment.

A piece is Haiku-suitable when **all** of these hold:

- Short and self-contained — no need to hold the wider design in mind
- Mechanical, with one correct answer: a rename, a mechanical edit across known files, an
  extraction, a lookup
- The orchestrator can **verify the result cheaply** — a grep, a build, a diff
- Getting it wrong is visible immediately, not three steps later

If the piece requires deciding *what* to do rather than *doing* a decided thing, it is not
Haiku work — no matter how small it is.

The orchestrator stays responsible for the outcome. Fanning work out does not transfer the
verification: check what came back rather than trusting the summary, because a small model
reporting success is exactly where a silent wrong answer enters.

**Fable is not used in this project.** It is reserved for major architectural decisions and
genuinely novel algorithms — neither of which this epic contains. Nothing here routes to it,
and no task should be labelled to reach for it.

**Fable is not used in this project.** It is reserved for major architectural decisions and
genuinely novel algorithms — neither of which this epic contains. Nothing here routes to it,
and no task should be labelled to reach for it.

If a task in this project appears to *need* Fable, treat that as a signal about the **story,
not the model**: it has been scoped too broadly, an unstated decision is buried inside it, or
it is missing context an implementer needs. Split it or clarify it rather than escalating
past Opus.

## Risk is a separate axis

Complexity is about reasoning demand. It is **not** the same as blast radius, and the two
come apart often:

- A find-and-replace across a public repo is complexity 1 and high risk — a mistake is
  public and permanent.
- A gnarly parser with full test coverage is complexity 5 and low risk — it fails loudly and
  locally.

So route on complexity, but gate on risk: anything touching published artifacts, credentials,
git history or CI configuration gets a human check regardless of how low its number is.

## Honesty about the number

The number is set when the story is written, before anyone knows how it will go. If a task
turns out to be a level harder than its label, **change the label and say so** — a stale
complexity field routes the next similar task wrong, and the mistake compounds quietly.

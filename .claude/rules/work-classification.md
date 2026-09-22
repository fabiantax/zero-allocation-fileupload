# Work classification (accounting)

Two fields on every story, for cost accounting and R&D tax administration.

## `work_type: new | maintenance`

| Value | Means |
|---|---|
| `new` | Builds capability that did not exist |
| `maintenance` | Keeps existing behaviour working — defect fixes, dependency bumps, refactors with no behaviour change |

This epic is greenfield, so everything is `new`. Recording that explicitly still matters: the
first `maintenance` item is the moment the project changes character, and it should be visible
when it happens rather than inferred later from commit archaeology.

## `wbso_candidate: true | false`

**A flag, not a determination.** The team marks work it *suspects* qualifies for WBSO; whoever
handles the application decides. Nothing here is tax advice, and an engineer's checkbox is not
an eligibility ruling.

Mark `true` when, **at the point the work started**, there was genuine technical uncertainty
resolved by systematic investigation — you did not know whether the approach would work, or
which approach would work, and finding out required experiment rather than lookup.

Mark `false` for work that is technically routine, however difficult or large:

| Not a candidate | Why |
|---|---|
| Choosing between known libraries | Selection, not investigation — the options' behaviour is documented |
| Implementing a documented pattern | No uncertainty at the outset, however intricate the code |
| Scaffolding, CI wiring, docs, tests | Supporting activity |
| A hard problem with a known method | Difficulty is not the criterion; **uncertainty** is |

The distinction that catches people out: **complexity and WBSO are not the same axis.** A
complexity-5 task using an established technique is not a candidate. A complexity-3 spike into
something nobody on the team has established is.

## Evidence

WBSO administration is audited, and the thing auditors want is contemporaneous evidence that
the uncertainty was real and the investigation was systematic. Two artifacts this project
already produces happen to be exactly that:

- **Spike tasks** — a stated unknown, a timebox, a method, and a recorded outcome, whether or
  not it succeeded. A spike that concluded "this does not work" is *better* evidence than one
  that succeeded, because it demonstrates genuine uncertainty.
- **ADRs** — the alternatives considered, what was tried, and why the choice was made.

Write these as you go. Reconstructed after the fact they are worth little, and an audit asks
for records from the period, not a summary written later.

**Hours:** the `Hours:` field in a task is an *estimate for planning*. WBSO claims are made on
hours actually worked and recorded. Do not let estimates leak into a claim — they are
different numbers with different consequences if wrong.

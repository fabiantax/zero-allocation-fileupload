# Splitting stories so agents can work in parallel

Problems this decomposition must not fall into, and the rule that closes each. This file is the
operative form.

## Declared file ownership

**Problem**: two stories that are conceptually independent still collide, because both write a
file neither of them is about — a solution file, a DI registration, a CI workflow. The collision
is invisible at planning time and shows up as a corrupted merge.
**Rule**: every story lists **the files it owns**. Two stories that are in flight together must
not own the same file. An overlap is a splitting defect, not a merge problem to handle later —
either merge the stories, or extract the shared file's change into its own story that lands
first.

## Vertical slices

**Problem**: slicing by layer reproduces the architecture's dependency chain as a work plan, so
every story waits on the one beneath it. It looks tidy because it matches the diagram, which is
exactly why it misleads.
**Rule**: prefer a slice that crosses layers for one behaviour ("reject an oversized upload end
to end") over one that completes a layer ("all validation"). Horizontal slices need a written
justification.

## Shared contracts split per consumer

**Problem**: a single "contracts" story blocks everything, and most consumers need only a
fraction of it.
**Rule**: split a contract story along the seams of **who consumes which part**. Each piece
unblocks its own stream as soon as it lands, instead of all streams waiting for all of it.

## Tests ship with their implementation

**Problem**: a separate test story depends on every implementation story it covers, converting
parallel streams back into one — and lets the implementation claim done without proof.
**Rule**: a story is done when it is tested. No story exists whose only content is testing
another story's work.

## Count what is runnable

**Problem**: a plan can look parallel while having a long serial prefix.
**Rule**: for each phase, record how many stories are **runnable** — all dependencies merged.
A run of `1, 1, 1, 5` is a defect in the plan, not a property of the work. State the count in
the epic so it is visible before execution, not after.

## Prefer fan-out to joins

**Problem**: a story depending on three predecessors runs at the speed of the slowest, and
extra agents do nothing.
**Rule**: when a story needs several predecessors, check whether it is really several stories.
Fuse only when the parts genuinely cannot ship separately.

## Cut scaffolding at the unblocking line

**Problem**: scaffolding stories block everything and are mostly mechanical.
**Rule**: split scaffolding so the part that unblocks others is as small as possible. Whatever
remains is an ordinary parallel story.

## Self-sufficiency is the precondition

**Problem**: a story written assuming shared context cannot be handed to an isolated agent —
the coupling is in the context, not the code.
**Rule**: every story carries its own context, file list, acceptance criteria and explicit
non-goals. If it needs a conversation to start, it is not ready.

## Parallelism is bounded by review, not by agents

**Problem**: N agents produce N diffs for one reviewer who must rebuild and re-verify each.
Verification does not parallelise with the work.
**Rule**: do not start more concurrent stories than can be reviewed as they land. Extra agents
beyond that convert waiting into work-in-progress, which is worse — it looks like progress.

## Keeping this file honest

**Problem**: a rules directory grows until agents skim it, and then it stops working — the
failure is silent, because skimmed rules and absent rules look identical in the output.
**Rule**: when this repo passes ten rules files, distil rather than append. Two rules that
close the same failure are one rule. A rule that has never been cited in a PR or a review is a
candidate for deletion, not for emphasis.

# Running tasks on Codex

Implementation is delegated to Codex (see `complexity-routing.md` for which model). This file
is how to invoke it so the job actually finishes. Every rule below is here because it failed
first.

## The invocation

```bash
codex exec \
  -C /abs/path/to/repo \
  --sandbox workspace-write \
  -c 'sandbox_workspace_write.network_access=true' \
  -m gpt-5.6-terra \
  -o /tmp/claude/<task>.result.md \
  - < /tmp/claude/<task>-prompt.txt \
  > /tmp/claude/<task>.log 2>&1
```

## Why each flag

**`- < promptfile` — always pipe the prompt on stdin.**
**Problem**: passing the prompt as a positional argument while stdin is a pipe (which it is for
any backgrounded command) makes Codex print `Reading additional input from stdin...` and wait
for an EOF that never comes. The job hangs forever, having done nothing, and looks identical to
a job that is working. This silently killed two tasks.
**Rule**: pass `-` and redirect the prompt file into stdin. If you must pass the prompt as an
argument, redirect `< /dev/null` explicitly.

**`-o <file>` — capture the final message.**
**Problem**: the run log is 100–250 KB of tool traces. Reading the verdict means parsing it.
**Rule**: `-o` writes only the agent's last message. That file appearing is also the reliable
signal that the run *completed* rather than died mid-way.

**`-C <dir>` — set the working root explicitly.**
**Problem**: a backgrounded command's `cd` does not persist, so relying on the session's cwd is
how a job ends up writing to the wrong place.

**`--sandbox workspace-write`** plus `network_access=true` when the task restores packages.
Read-only is right for reviews, wrong for implementation.

**Run Codex with Claude's own sandbox bypassed.** Codex applies its *own* macOS Seatbelt
sandbox, and Seatbelt cannot nest: inside Claude's sandbox it fails with
`sandbox_apply: Operation not permitted` and does nothing. Codex still sandboxes itself — the
bypass is what lets it.

## Detecting a dead job

`scripts/status.sh` shows every job log's size and last-write time. **A log that stops growing
is the signal** — not the absence of an error, because a hung job produces no error at all.
A job with no `-o` result file and a static log is dead; relaunch it, do not wait.

## What the prompt must contain

- **Settled decisions, stated as settled**, so the model does not re-litigate them. Name the
  ADR. Anything it has to re-derive it may re-derive differently.
- **The file paths it may touch**, and the ones it may not. Concurrent tasks must not share
  files.
- **`Do NOT commit`** — the orchestrator owns git, so the diff gets reviewed before it lands.
- **The LOC ceiling**, since PR size is a merge gate.
- **"Report the actual command output. Do not claim anything you did not run."** Without this
  you get plausible summaries of commands that were never executed.

## Verification does not transfer

Re-run the build and tests yourself and read the diff. Delegation moves the typing, not the
responsibility — and a model reporting success is exactly where a silent wrong answer enters.

Already caught this way: a hallucinated SDK version that got pinned into CI, and a test project
named after a project that does not exist.

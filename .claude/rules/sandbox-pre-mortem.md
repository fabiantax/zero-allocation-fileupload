# Sandbox pre-mortem

Claude Code's bash sandbox blocks local sockets, most network egress, and process
introspection. Nothing here announces itself as a sandbox problem: **the failure mode is a
hang, a timeout, or a wrong answer, never an error naming the sandbox.** Every entry below is
either something that already bit us or the same mechanism aimed at work still to come.

## The rule that prevents most of it

**Run `dotnet`, `git`, `gh` and `codex` with the sandbox bypassed.** Measured: `dotnet build`
takes **2s** outside and **60–300s** inside, where it eventually fails with
`MSB1025 / SocketError`. There is no partial-credit version of this — an inside-sandbox build
is not "slower", it is broken in a way that wastes minutes before telling you.

## Already hit

| What | Looked like | Actually |
|---|---|---|
| `dotnet build` | AOT being slow; then MSBuild node reuse; then NuGet restore | MSBuild worker nodes talk over sockets. Three wrong diagnoses before the error was read in full |
| `gh` / `git push` | Invalid auth token, TLS `x509: OSStatus -26276` | Keychain access blocked |
| `codex exec` | Ran, produced nothing | Codex applies its **own** Seatbelt sandbox; Seatbelt cannot nest |
| `ps`, `pgrep`, `sysctl` | Empty output | Process introspection blocked — **the liveness checks meant to detect silent failure fail silently themselves** |
| `$TMPDIR` | File written, then "no such file" | `$TMPDIR` differs between sandboxed and bypassed runs. Lost a task-issue map and a PR body this way. Use `/tmp/claude` for anything read back across calls |

## Predicted, for work not yet done

| Upcoming task | What will break | Why |
|---|---|---|
| **003, 006 — unit tests** | `dotnet test` hangs, then times out | VSTest runs the test host as a **separate process** and talks to it over a socket. Already observed at 180s and 301s |
| **007 — ArchUnitNET** | Restore hangs on the new package | `api.nuget.org` is not in the sandbox allowlist. Any task adding a `PackageReference` hits this |
| **008 — BenchmarkDotNet** | Fails or hangs on every benchmark | BenchmarkDotNet **generates, builds and spawns a child process per benchmark**, with IPC back to the host. It is the single most sandbox-hostile tool in the stack |
| **012 — coverage gate** | Coverage file never appears; gate reads nothing | The coverlet collector attaches to the test host over the same channel VSTest uses. Failure mode is an *empty report*, not an error — so a 0% gate would pass and a ratcheted gate would fail mysteriously |
| **012 — `WebApplicationFactory`** | Possibly fine, possibly hangs | It binds an in-memory server; `allowLocalBinding: true` is set, but Kestrel on a real port is a different path |
| **Running the published binary** | `curl` to `127.0.0.1` refused | Needs both local bind and local connect. The AOT spike only worked because Codex ran outside the sandbox |
| **Any `gitleaks` rule refresh** | Silent no-op | Network fetch blocked; a secret scanner that fetches nothing still exits 0 |

## The meta-risk

A coverage gate reading an empty report **passes**. A secret scanner that fetched no rules
**passes**. A benchmark that never ran produces no measurement to contradict. The sandbox
converts "did not run" into "no complaint", and every quality gate in this repo is built on the
assumption that silence means success.

**So: a gate that has never been seen to fail is not known to work.** Before trusting any new
gate, make it fail on purpose once — break a test, commit a fake secret, drop coverage — and
confirm it goes red.

## Triage

A command that is slow, silent, or returns empty where output was expected is a sandbox
suspect **first**, before any hypothesis about the code. Check in this order:

1. Re-run it with the sandbox bypassed. If it is suddenly fast or correct, that was it.
2. Read the **whole** error, not the tail. `MSB1025` sat under a stack trace I was truncating.
3. Check whether the tool spawns a child process or opens a socket. If it does, assume the
   sandbox until proven otherwise.

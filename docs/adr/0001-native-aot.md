# 1. Native AOT

- Status: **Proven, parked** (feasibility accepted; not enabled)
- Date: 2026-09-22

## Parked — 2026-09-22

AOT is **proven to work and deliberately not enabled.** `PublishAot` is set nowhere: not in
the csproj, not in CI.

The reason is scope, not build cost. **An earlier version of this section blamed AOT for a
five-minute build. That was wrong** — removing `PublishAot` left the build at 300s, and the real
cause was MSBuild's out-of-process worker nodes being blocked in a sandboxed shell, which hangs
until a 300-second timeout. With `-nodeReuse:false` the same build takes **0.19s**. AOT cost
nothing measurable.

It stays parked on its own merits: the assignment's budget is a couple of hours, a native publish
profile is an optimisation nobody asked for, and the feature itself is not written yet. The
feasibility question — the only one that was genuinely risky — is answered below.

Nothing is lost by waiting: the spike below proves the whole stack publishes natively with zero
warnings, so re-enabling it is a one-line change plus a CI publish step. The measurements stand
as evidence whether or not the flag is on.

## Correction

The spike's own report claimed the SDK was `10.0.401`. That was wrong — no such SDK is installed
on this machine, and `dotnet --version` reports `10.0.301`. The number was corrected here, and
in the CI pin that had inherited it. Every other figure below was re-checked against the command
output in the spike log.

## Context

The spike tested whether a .NET 10 minimal API can be published as a native `osx-arm64`
executable while serving both a generated OpenAPI document and the Scalar browser UI.

The test application used:

- SDK `10.0.301` (verified with `dotnet --version`; installed SDKs are 8.0.420, 10.0.202,
  10.0.300, 10.0.301)
- target framework `net10.0`
- `Microsoft.AspNetCore.OpenApi` `10.0.12`
- `Scalar.AspNetCore` `2.17.8`
- `WebApplication.CreateSlimBuilder`
- a `JsonSerializerContext` containing the endpoint's `HelloResponse` type
- `<PublishAot>true</PublishAot>`

After `dotnet clean -c Release -r osx-arm64`, this command was run:

```text
dotnet publish -c Release -p:PublishAot=true -r osx-arm64
```

Its complete output was:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  aotspike -> /private/tmp/claude/spike/aotspike/bin/Release/net10.0/osx-arm64/aotspike.dll
  Generating native code
  aotspike -> /private/tmp/claude/spike/aotspike/bin/Release/net10.0/osx-arm64/publish/
```

Warning result: **zero warnings**. There were no `IL2xxx`, `IL3xxx`, or `RDG` warnings to
record verbatim, so the warning count and warning-ID count were both zero.

The published `aotspike` file was reported by `file` as `Mach-O 64-bit executable arm64`.
`stat` reported `13,791,880` bytes (`13.153 MiB`) for that binary.

The published executable was started directly, not through `dotnet run` or a test host. In
this restricted runner it was launched with
`DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false`; without that setting, an instrumented
run reached the call to `WebApplication.CreateSlimBuilder` but did not return from it or
bind a socket during a 10-second probe. With the setting, the native process logged that it
was listening on the requested loopback URL. No reflection-enabling switch was used.

Against the final native process, `curl` measured:

| Route | Status | Content type | Response size |
|---|---:|---|---:|
| `/openapi/v1.json` | 200 | `application/json;charset=utf-8` | 919 bytes |
| `/scalar/v1` | 200 | `text/html` | 624 bytes |
| `/scalar/scalar.js` | 200 | `text/javascript` | 4,312,689 bytes |
| `/scalar/scalar.aspnetcore.js` | 200 | `text/javascript` | 2,632 bytes |
| `/hello` | 200 | `application/json; charset=utf-8` | 32 bytes |

The OpenAPI response identified version `3.1.1`, contained `/hello`, and assigned it the
operation ID `GetHello`. The Scalar HTML contained the title `Scalar API Reference`, loaded
both JavaScript routes above, and configured `openapi/v1.json` as its document source.

Cold start was measured from spawning the native executable to receiving and reading a
successful `200` response from `/openapi/v1.json`. Ten new processes produced these times,
in milliseconds:

```text
434.896, 20.562, 20.098, 18.413, 18.501,
16.688, 16.786, 16.428, 16.507, 16.704
```

The arithmetic mean was **59.558 ms**; the median was `17.600 ms`, with a minimum of
`16.428 ms` and maximum of `434.896 ms`. The mean includes the first post-publish launch.

## Decision

Adopt Native AOT for the epic. The answer to the spike question is **yes**: this package
stack both publishes with `PublishAot=true` and serves a browser OpenAPI UI from the native
binary.

The package stack for subsequent work is `Microsoft.AspNetCore.OpenApi` for document
generation and `Scalar.AspNetCore` for the UI, with System.Text.Json source-generated
metadata retained for API payload types. The versions exercised by this decision are
`10.0.12` and `2.17.8`, respectively.

## Consequences

- Native AOT remains enabled; there is no AOT-driven downstream task closure.
- OpenAPI and Scalar introduced no trim, AOT, or request-delegate-generator warnings in
  the clean publish performed for this spike.
- The measured native executable is `13,791,880` bytes, before considering debug symbols
  or the accompanying JSON files in the publish directory.
- The ten-process cold-start mean to a completed OpenAPI response is `59.558 ms` in this
  runner. The individual measurements are retained above because the first run was much
  slower than the following nine.
- Reproducing startup inside this restricted runner requires disabling host configuration
  reload. That setting was used only to make the published process start in the runner;
  it did not change the publish result or enable reflection.

## Alternatives considered

- **Drop Native AOT:** rejected because the clean AOT publish completed with zero relevant
  warnings and the resulting native process served the document, UI HTML, UI assets, and
  endpoint successfully.
- **Choose another OpenAPI UI package:** not pursued because the tested Scalar package
  served its HTML and both referenced JavaScript assets successfully from the native
  process.

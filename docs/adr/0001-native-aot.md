# 1. Native AOT

## Status

**Proven, parked** — feasibility was demonstrated on 2026-09-22, but Native AOT is deliberately
not enabled in any project or in CI.

## Context

Native AOT was the riskiest compatibility question because the service also needs generated
OpenAPI, a browser UI, and JSON error responses. The feasibility spike therefore tested the
actual package stack before feature work depended on it. The original spike decision was to
adopt AOT; the later implementation PR parked it on scope grounds. That chronology is recorded
in the [decision log](../decision-log.md#21-native-aot) and in merged PR #21 (`b5d7d8f`).

The spike used SDK `10.0.301`, `net10.0`, `Microsoft.AspNetCore.OpenApi` `10.0.12`,
`Scalar.AspNetCore` `2.17.8`, `WebApplication.CreateSlimBuilder`, source-generated JSON metadata,
and `<PublishAot>true</PublishAot>`. This command completed with no `IL2xxx`, `IL3xxx`, or `RDG`
warnings:

```text
dotnet publish -c Release -p:PublishAot=true -r osx-arm64
```

The output was a `13,791,880` byte (`13.153 MiB`) arm64 Mach-O executable. The executable itself,
not `dotnet run`, served the OpenAPI document, Scalar HTML and JavaScript assets, and a JSON
endpoint. Ten process launches reached a completed OpenAPI response with a median of `17.600 ms`;
the recorded samples were:

```text
434.896, 20.562, 20.098, 18.413, 18.501,
16.688, 16.786, 16.428, 16.507, 16.704
```

Two corrections matter. The spike report originally named SDK `10.0.401`; verification showed
that version was not installed, and both this record and CI were corrected to `10.0.301`. An
earlier revision also blamed AOT for a five-minute build. Removing `PublishAot` did not remove
the delay: sandboxed MSBuild worker-node communication was timing out. `-nodeReuse:false` fixed
the build. The incident therefore provides no evidence that AOT caused the delay. Both
corrections are recorded in PR #21 and commit `b5d7d8f`.

The restricted spike runner needed `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` to let the
published process bind a socket. No reflection-enabling switch was used.

## Decision

Keep the proven, trim-compatible package and serialization choices, but park Native AOT. The
assignment budgets a couple of hours and asks for the file transformation, not a native
deployment profile. Feasibility was worth resolving; carrying RID-specific publish settings and
a native CI smoke test into the submitted implementation was not.

`PublishAot` is consequently absent from every project and from CI. Reconsider the decision only
when a deployment requirement supplies a measurable cold-start, memory, or distribution target.
Re-enabling it requires the project property, a RID-specific publish, promotion of relevant
IL/RDG warnings to errors, and a smoke test of the published executable including OpenAPI and
Scalar. If a future UI package conflicts with AOT, the required browser UI wins and AOT remains
off.

## Consequences

- The risky compatibility question is answered and the selected OpenAPI/Scalar stack has native
  publish evidence even though the production projects remain ordinary framework-dependent .NET
  applications.
- Source-generated JSON and trim-friendly dependencies remain useful constraints, but AOT
  analyzers and native publication do not gate normal builds.
- The service gives up the possible cold-start, resident-memory, and self-contained-deployment
  benefits of AOT. Those benefits were not measured against a JIT build and are not claimed.
- There is no native artifact or RID matrix in CI. A future decision to enable AOT must pay that
  build, test, debugging, and distribution cost explicitly.
- Scalar remains a third-party dependency, and the spike proves only the versions and routes
  exercised above; an upgrade needs its own publish-and-run check before relying on AOT again.

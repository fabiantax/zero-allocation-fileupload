# 4. Solution structure, domain modelling, and ports

## Status

**Accepted** — 2026-09-22.

## Context

The implementation has four production projects. Domain contains the filename value object,
suffix policy, mutation context, and two dependency-inversion ports. Application owns the
HTTP-independent file acceptance decision. Infrastructure implements time/random suffix mutation.
Api parses multipart input, maps errors, composes dependencies, and writes the response.

The operation has no entity identity, state transition, or invariant spanning an object graph.
It also has no persistence requirement. The [decision log](../decision-log.md) records why an
uploaded-file aggregate would be ceremonial and why HTTP/OpenAPI is the boundary for other domains.
The independent design review of 2026-09-22 identified the planned repository and registry as
speculative. Storage and dispatch were removed, and `IMutateFileUseCase` was deleted because
nothing implemented or called it.

## Decision

Keep the four inward-pointing projects and use ports only where a current dependency must be
inverted: `IFileMutator` separates the policy-facing contract from pipeline mechanics, and
`IRandomSequenceGenerator` makes nondeterminism replaceable. Keep acceptance as a plain
Application function with no ASP.NET Core types. Let the endpoint coordinate the single command
directly instead of adding a delegating use-case or mediator layer.

Do not create an aggregate root. A value object plus a pure policy is the full domain model this
stateless transformation warrants. Do not create a storage port: there is no entity to save and
no lifecycle to retrieve. Consuming domains depend on the HTTP/OpenAPI contract rather than
referencing internal domain assemblies.

An aggregate would become appropriate if files acquired stable identity and lifecycle rules—for
example versioning, ownership, retention, or transitions that must be kept consistent. A storage
port would become appropriate only when a use case needs durable state.

## Consequences

- Domain and Application remain independent of ASP.NET Core and concrete I/O adapters, and time
  and randomness are deterministic in tests.
- The absence of an aggregate is itself a modelling decision, not incomplete DDD.
- Four projects are organizational overhead for a small feature, and navigating the operation
  crosses more files than a single-project implementation would.
- The endpoint performs orchestration directly. If cross-cutting command behaviors or several
  commands appear, the lack of an application use-case layer may need revisiting.
- Consumers pay the cost of an HTTP integration instead of reusing domain types, but internal
  refactoring and release cycles stay independent.
- Adding persistence later is not a plug-in swap; its semantics must first be modelled and the
  appropriate application-facing port introduced.

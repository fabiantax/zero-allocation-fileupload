# 7. Testing strategy

## Status

**Accepted; gates are landing incrementally** — 2026-09-22.

## Context

The assignment values evidence of engineering judgment, so “well tested,” “clean architecture,”
and “low allocation” must be separate, checkable claims. The
[decision log](../decision-log.md#31-branch-coverage-not-line-coverage) records the three-part
strategy: branch coverage for behavior, executable architecture rules for dependencies, and
BenchmarkDotNet for allocation. PR #21 (`b5d7d8f`) introduced the CI collector and a threshold-0
scaffold. Architecture commit `f501045` added both ArchUnitNET type-dependency rules and direct
`.csproj` checks, and deliberately demonstrated that a forbidden unused project reference passes
ArchUnitNET but fails the project-file rule.

At this ADR's creation, the current checkout still has the coverage threshold at 0% and the
architecture commit is on its feature branch. This record does not claim those parallel changes
have already merged.

## Decision

Use xUnit unit tests for pure domain/application behavior and deterministic mutation, plus
`WebApplicationFactory` integration tests for multipart parsing, status mapping, and response
ordering. Measure **branch coverage**, not line coverage, because conditional outcomes are the
useful signal; the intended CI gate is at least 80% branch coverage, with critical multipart,
boundary, cancellation, and streaming paths tested explicitly regardless of the aggregate.

Treat architecture rules as tests. ArchUnitNET checks compiled type and namespace dependencies;
a companion test reads project files because an unused forbidden `ProjectReference` is invisible
in bytecode. A gate is trusted only after a controlled violation has made it fail.

Keep BenchmarkDotNet in a separate benchmark project, run in Release mode, outside `dotnet test`
and the coverage-gated suite. `[MemoryDiagnoser]` and a file-size sweep answer allocation questions;
they are not correctness tests and should not make ordinary CI timing depend on a quiet machine.

## Consequences

- Behavior, dependency direction, and allocation are verified by tools suited to each claim
  rather than collapsed into one misleading quality number.
- Branch coverage makes untested decision paths more visible than line coverage, and architecture
  drift becomes a failing test rather than a review convention.
- The 80% aggregate is not proof of correct behavior; important paths still require named tests,
  and generated or trivial code can distort the percentage.
- Architecture tests protect compile-time structure, not runtime behavior or design quality.
- Benchmarks are deliberately not part of the default test/coverage gate, so allocation regressions
  are not caught unless the benchmark workflow is run and compared under controlled conditions.
- More test projects, packages, and CI stages increase maintenance and restore time for a very
  small production codebase.

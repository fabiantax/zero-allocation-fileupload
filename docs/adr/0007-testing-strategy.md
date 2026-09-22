# 7. Testing strategy

## Status

**Accepted** — 2026-09-22.

## Context

“Well tested,” “clean architecture,” and “low allocation” are different claims, so each needs a
check suited to what it asserts rather than one aggregate quality score.

## Decision

Use xUnit unit tests for pure domain/application behavior and deterministic mutation. Use
`WebApplicationFactory` for the HTTP boundary — multipart parsing, status mapping, response
headers, and response ordering — so those tests exercise the actual host rather than a re-created
approximation of it.

Measure **branch coverage**, not line coverage, because conditional outcomes are the useful
signal: a method can execute every statement while still taking only one side of every decision.
CI gates branch coverage at 80%, while multipart boundaries, cancellation, and streaming paths
are named explicitly regardless of the aggregate.

Keep BenchmarkDotNet in a separate benchmark project, run it in Release mode, and keep it outside
`dotnet test` and the coverage-gated suite. `[MemoryDiagnoser]` and a file-size sweep answer
allocation questions; they are not correctness tests and should not make ordinary CI timing depend
on a quiet machine.

## Consequences

- Behavior and allocation are verified by tools suited to each claim rather than collapsed into
  one misleading quality number.
- Branch coverage makes untested decision paths more visible than line coverage.
- The 80% aggregate is not proof of correct behavior; important paths still require named tests,
  and generated or trivial code can distort the percentage.
- Benchmarks are not part of the default test/coverage gate, so allocation regressions
  are not caught unless the benchmark workflow is run and compared under controlled conditions.

# Architecture

These diagrams describe the **designed** architecture. No code exists yet — they are the
contract tasks 001–004 build against, and they are expected to be corrected against reality
once those tasks land (task 010 owns that pass).

Source of truth for the decisions behind them: `.claude/epics/file-mutation-api/epic.md`.

## 1. Structure — layers, ports and adapters

The dependency rule is the point of this diagram: **arrows only ever point inward**. Domain
and Application reference neither Infrastructure nor Api. `FileMutation.Architecture.Tests`
(task 007) enforces this with ArchUnitNET, so it is a build failure rather than a convention.

```mermaid
graph TB
    subgraph consumers["Consumers"]
        UI["Scalar OpenAPI UI<br/>browser"]
        SYS["Other domains<br/>bind to the HTTP contract only"]
    end

    subgraph api["FileMutation.Api — host and composition root"]
        EP["FileMutateEndpoint<br/>multipart in, file out"]
        OAPI["OpenAPI document + Scalar UI"]
        PD["IExceptionHandler<br/>RFC 7807 ProblemDetails"]
        DI["DI container<br/>ValidateOnBuild / ValidateScopes"]
    end

    subgraph app["FileMutation.Application — orchestration"]
        UC["MutateFileUseCase"]
        VAL["Validation rules<br/>.txt · text/plain · UTF-8 decode"]
    end

    subgraph dom["FileMutation.Domain — policy, no I/O, no framework refs"]
        FN["FileName<br/>value object, always valid"]
        POL["Mutation policy<br/>pure fn over Span&lt;byte&gt;"]
        P1(["IFileMutator<br/>Format + MutationCapability"])
        P0(["IFileMutatorRegistry"])
        P2(["IFileRepository"])
        P3(["IRandomSequenceGenerator"])
    end

    subgraph infra["FileMutation.Infrastructure — adapters"]
        MUT["DateAndRandomSequenceMutator<br/>Format: PlainTextUtf8 · Streaming"]
        REG["FormatRegistry<br/>one entry today"]
        REPO["LocalDiskFileRepository<br/>deferred — task 005"]
        RNG["CryptoRandomSequenceGenerator"]
    end

    TP["TimeProvider<br/>built into .NET 8+"]

    UI --> EP
    SYS --> EP
    EP --> OAPI
    EP --> VAL
    EP --> UC
    EP -.-> PD
    VAL --> FN
    UC --> P0
    P0 -. resolves .-> P1
    UC --> P1
    UC --> P2

    MUT -. implements .-> P1
    REG -. implements .-> P0
    REPO -. implements .-> P2
    RNG -. implements .-> P3

    MUT --> POL
    MUT --> P3
    MUT --> TP

    DI -. wires .-> REG
    DI -. wires .-> MUT
    DI -. wires .-> REPO
    DI -. wires .-> RNG

    classDef deferred stroke-dasharray: 5 5
    class REPO deferred
```

**Why the split between `POL` and `MUT` matters.** Domain owns *what* is appended and in what
format, as a pure function over spans. Infrastructure owns the `Pipelines` mechanics that feed
it. That keeps Domain I/O-free (Clean Architecture) *and* makes the hot path testable against a
stack-allocated span with no streams — the two goals reinforce rather than compete.

Format identity sits **on `IFileMutator`**, not on a second port — a parallel interface would
have taken the same inputs and returned the same outputs. `MutationCapability` (`Streaming` or
`BufferedRewrite`) is what stops a future container-format adapter from hiding that it must
buffer the whole file. The registry resolves one adapter or returns a rejection; it has no
fallback, so an unmatched format cannot silently become plain text. See ADR-0009.

There is deliberately **no `UploadedFile` aggregate root**. The operation is a stateless
transformation with no identity, lifecycle, or cross-entity invariants; inventing an aggregate
to look DDD-shaped would be cargo cult. See the epic's "Domain Modelling Stance".

## 2. Flow — one upload request, end to end

Note the ordering: the size limit is enforced by Kestrel **before the body is read**, so an
oversized upload never reaches the endpoint. A redundant length check downstream would never
fire.

```mermaid
sequenceDiagram
    autonumber
    actor C as Client / Scalar UI
    participant K as Kestrel request limits
    participant E as FileMutateEndpoint
    participant V as Validation
    participant R as IFileMutatorRegistry
    participant U as MutateFileUseCase
    participant M as DateAndRandomSequenceMutator
    participant D as Domain mutation policy
    participant X as IExceptionHandler

    C->>K: POST multipart/form-data

    alt body exceeds MaxRequestBodySize
        K-->>C: 413 ProblemDetails — body never read
    else within limit
        K->>E: forward request
        alt file field missing or empty
            E->>X: validation failure
            X-->>C: 400 ProblemDetails
        else file present
            E->>V: extension .txt, content-type text/plain
            alt outside the accepted set
                V->>X: unsupported media type
                X-->>C: 415 ProblemDetails
            else accepted
                V-->>E: FileName value object
                E->>R: resolve(FileName, contentType)
                alt no adapter matches
                    R-->>X: unsupported format, no fallback
                    X-->>C: 415 ProblemDetails
                else resolved
                R-->>E: IFileMutator, Capability Streaming
                E->>U: Mutate(PipeReader, PipeWriter, FileName)
                U->>M: through IFileMutator port
                loop each ReadOnlySequence segment
                    M->>M: decode incrementally, carry partial UTF-8 across the boundary
                    M->>M: copy segment to PipeWriter — no whole-file buffer
                end
                alt invalid UTF-8 sequence
                    M-->>X: decode failure
                    X-->>C: 415 ProblemDetails
                end
                M->>D: append into span — utcNow, randomSequence
                D-->>M: bytes written
                M-->>U: flush complete
                U-->>E: done
                E-->>C: 200 + Content-Disposition, original filename
                end
            end
        end
    end
```

Every failure path lands on a single `IExceptionHandler` rather than per-endpoint `try/catch` —
that is what keeps *all* errors emerging as `ProblemDetails` instead of most of them doing so
and one leaking a stack trace.

Validation failures return a result; they do not throw. A rejected upload is an expected
outcome, and throwing on it costs an allocation plus a stack unwind on a hot path.

## 3. State machine — request lifecycle

```mermaid
stateDiagram-v2
    [*] --> Received

    Received --> Rejected413: over size limit
    Received --> Validating: within limit

    Validating --> Rejected400: missing or empty file field
    Validating --> Rejected415: not .txt or not text/plain
    Validating --> Resolving: FileName constructed

    Resolving --> Rejected415: no adapter matches, no fallback
    Resolving --> Streaming: adapter resolved

    Streaming --> Mutating: segments copied and decoded
    Streaming --> Rejected415: invalid UTF-8 sequence
    Streaming --> Faulted: read or write fault
    Mutating --> Completed: date and sequence appended
    Mutating --> Faulted: policy fault

    Completed --> [*]: 200 + Content-Disposition
    Rejected400 --> [*]: 400 ProblemDetails
    Rejected413 --> [*]: 413 ProblemDetails
    Rejected415 --> [*]: 415 ProblemDetails
    Faulted --> [*]: 500 ProblemDetails, no stack trace

    note right of Rejected413
        Terminal before the body is read.
        Kestrel MaxRequestBodySize and
        MultipartBodyLengthLimit.
    end note

    note right of Streaming
        No state is retained between requests.
        Persistence is a deferred port, not a
        lifecycle stage — see PRD OQ-1.
    end note

    note right of Resolving
        Accepted set is .txt only.
        Decoding cleanly is necessary,
        not sufficient — see ADR-0009.
    end note
```

The machine has **no persisted state**: every terminal transition ends the request, and nothing
survives it. That is what makes `IFileRepository` a genuinely optional port rather than a hole
in the design.

## Mapping to the task breakdown

| Diagram element | Built by |
|---|---|
| API host, OpenAPI document, Scalar UI | 001 |
| `FileName`, mutation policy, ports, `FileFormat`, `MutationCapability`, registry | 002 |
| `DateAndRandomSequenceMutator`, segment handling | 003 |
| Endpoint, validation, `IExceptionHandler`, `Content-Disposition` | 004 |
| `LocalDiskFileRepository` | 005 — deferred, blocked on OQ-1/OQ-2 |
| Dependency-rule enforcement | 007 |

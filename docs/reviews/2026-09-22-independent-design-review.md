# Independent design review — 2026-09-22

Run against the planning artifacts before any code was written, using an **independent model**
(OpenAI `gpt-5.6-sol` via Codex) rather than the one that produced them. The prompt asked for
adversarial findings and explicitly told it to skip anything it could not substantiate.

It is kept verbatim, including the findings that are unflattering, because a review with the
criticism removed is not evidence of anything. Responses and what was changed are tracked in
`docs/decision-log.md`.

---

## 1. Technical errors

1. **High — [decision-log.md:103](decision-log.md:103), [architecture.md:99](architecture.md:99)**  
   **Claim:** “`MaxRequestBodySize` plus `MultipartBodyLengthLimit` — before the body is read”; a downstream length check is “dead code.”  
   **Wrong:** `MultipartBodyLengthLimit` is enforced while multipart sections are parsed; it can throw after reading has begun. Kestrel also cannot pre-reject a chunked request whose size is not known. Moreover, Kestrel limits the whole request, while the multipart limit applies to a part; setting both to the nominal file limit rejects a valid maximum-size file because of multipart overhead. [Microsoft’s upload documentation](https://learn.microsoft.com/aspnet/core/mvc/models/file-uploads) explicitly describes parsing-time enforcement.  
   **Should say:** “Configure a whole-request ceiling and independently count the selected part’s bytes while parsing. Some oversized requests can be rejected from `Content-Length`; others fail during reading. Test chunked input and a file exactly at `MaxFileBytes`.”

2. **High — [architecture.md:140](architecture.md:140)**  
   **Claim:** Copy each segment to the response `PipeWriter`, then, on invalid UTF-8, return “415 ProblemDetails.”  
   **Wrong:** Once output is flushed, headers/status may already be committed. Exception handling cannot re-execute or replace a started response. [ASP.NET Core documents this limitation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0). The client instead receives a truncated 200 or a reset connection.  
   **Should say:** Choose one:

   - Validate/spool first, then send a successful response.
   - Stop validating UTF-8 and treat the upload as opaque bytes.
   - Accept non-atomic streaming and document that failures after response start abort the body and cannot return `ProblemDetails`.

3. **High — [architecture.md:123](architecture.md:123), [architecture.md:163](architecture.md:163)**  
   **Claim:** Validation returns a result rather than throwing, but validation failures go through `IExceptionHandler`.  
   **Wrong:** `IExceptionHandler` handles exceptions raised through exception-handler middleware. It is not a dispatcher for result-based validation failures.  
   **Should say:** “Expected validation failures directly execute/return `TypedResults.Problem`; `IExceptionHandler` handles unexpected exceptions only, and only before the response starts.”

4. **High — [architecture.md:138](architecture.md:138), [epic.md:32](epic.md:32)**  
   **Claim:** Await `Mutate(PipeReader, PipeWriter, …)`, then return `Results.File`.  
   **Wrong:** Pipe ownership and execution are unspecified. If this is an internal bounded `Pipe`, awaiting the producer before returning the result can deadlock on backpressure because no consumer is executing yet. If it is `Response.BodyWriter`, the response starts during mutation and `Results.File` is no longer the response mechanism. .NET 10 has `Results.Stream(PipeReader, …, fileDownloadName:)`, but its producer must run concurrently and complete/fault the pipe correctly. [Minimal API response documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-10.0)  
   **Should say:** Specify whether the writer is the HTTP response or an internal pipe, who starts the producer, who completes both ends, and how producer faults are observed.

5. **Medium — [file-mutation-api.md:93](file-mutation-api.md:93)**  
   **Claim:** “Library projects set `IsAotCompatible` so violations fail at build.”  
   **Wrong:** `IsAotCompatible` enables analyzers; their diagnostics are warnings unless warnings are promoted to errors. RDG diagnostics likewise do not inherently stop the build. [Native AOT documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot)  
   **Should say:** “Enable AOT/trim analyzers and explicitly fail CI on relevant IL/RDG warnings; publish and run the native artifact.”

6. **Medium — [epic.md:46](epic.md:46)**  
   **Claim:** “AOT demands reflection-free, trim-safe code; so does a zero-allocation hot path … AOT analyzers catch violations.”  
   **Wrong:** Allocation behaviour and AOT compatibility are independent. AOT analyzers diagnose dynamic-code/trimming hazards, not managed allocations. Reflection-free code can allocate heavily; allocation-free code can be trimming-unsafe.  
   **Should say:** “AOT compatibility and allocation are separate constraints, verified by separate tests.”

7. **Medium — [epic.md:28](epic.md:28)**  
   **Claim:** “Reflection-based serialisation does not survive trimming.”  
   **Wrong:** Too categorical. Reflection-based `System.Text.Json` is disabled by default for trimmed publication because it is unsafe/unpredictable, not because all reflection serialization is physically impossible. Source generation is the correct choice, but the explanation is inaccurate. [System.Text.Json source-generation documentation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)  
   **Should say:** “Use generated metadata for every application JSON type because general reflection metadata cannot be relied upon after trimming/AOT.”

8. **Medium — [ADR-0009:56](0009-accepted-formats-and-mutator-dispatch.md:56)**  
   **Claim:** A `.docx` “must buffer the whole archive” because the ZIP central directory is at the end.  
   **Wrong:** `ZipArchiveMode.Update` holds the archive in memory, but that is an implementation limitation, not a ZIP-law requirement. An archive can be rewritten into a new output sequentially using seekable temporary storage or a suitable streaming reader/writer. `ZipArchiveMode.Create` itself supports non-seekable output. [ZipArchive documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchive.-ctor?view=net-10.0)  
   **Should say:** “The selected .NET/OpenXML approach would require seekable storage or buffering and is disproportionate; `.docx` is also outside the text-file requirement.”

9. **Medium — [epic.md:24](epic.md:24)**  
   **Claim:** ArchUnitNET mechanically enforces that Domain/Application have no compile-time dependency on outer projects.  
   **Wrong:** ArchUnitNET primarily analyzes type/member dependencies in compiled assemblies. An unused forbidden `ProjectReference` need not create such a dependency. [ArchUnitNET describes its bytecode/type analysis](https://github.com/TNG/ArchUnitNET).  
   **Should say:** “Use ArchUnitNET for type/namespace dependency rules and a separate project-reference/MSBuild check for the `.csproj` graph.”

10. **Medium — [file-mutation-api.md:53](file-mutation-api.md:53)**  
    **Claim:** “All error responses are RFC 7807 `ProblemDetails`.”  
    **Wrong:** Kestrel-level rejection, malformed HTTP, disconnects, cancellation, and failures after response start are not under that guarantee.  
    **Should say:** “Application-generated errors before response start use `ProblemDetails`; transport failures and mid-stream failures may terminate the connection without a structured body.”

## 2. Unjustified claims

1. **High — [decision-log.md:278](decision-log.md:278)**  
   **Claim:** BenchmarkDotNet makes “no per-request allocation that scales with file size” checkable.  
   **Problem:** No benchmark boundary, input-size sweep, warm-up state, or concurrency model is defined. A mutator-only benchmark omits multipart parsing, headers, error objects, Kestrel and pipe creation. `MemoryDiagnoser` also does not establish bounded retained memory or absence of LOH pool growth under concurrency.  
   **Should say:** “Measure allocated bytes for several file sizes and report the slope; separately run an end-to-end concurrent memory test and state exactly what is excluded.”

2. **Medium — [epic.md:43](epic.md:43)**  
   **Claim:** Native AOT makes first-request latency predictable, lowers RSS materially, and makes scale-to-zero viable.  
   **Problem:** There is no deployment target, baseline, cold-start budget, instance count or JIT comparison. These are plausible benefits, not established requirements.  
   **Should say:** “AOT is an experiment. Keep it only if measurements against framework-dependent JIT deployment justify its package and build constraints.”

3. **Medium — [epic.md:55](epic.md:55)**  
   **Claim:** Task 000 settles Scalar/AOT compatibility “by publishing.”  
   **Problem:** Publishing proves only compilation. The native binary must start, serve the OpenAPI document, render Scalar, show a multipart file control, submit a file, and download the response. `Microsoft.AspNetCore.OpenApi` is officially Native-AOT compatible, but the visual UI remains a third-party integration. [Microsoft OpenAPI documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0)  
   **Should say:** “Run a smoke test against the published native executable, with zero AOT warnings, including the browser upload/download path.”

4. **Medium — [file-mutation-api.md:85](file-mutation-api.md:85)**  
   **Claim:** Pipelines imply “tens of concurrent uploads without unbounded memory growth.”  
   **Problem:** Pipelines provide mechanisms for pooling and backpressure; correct bounds still depend on pause/resume thresholds, `AdvanceTo`, slow clients, concurrency limits and disposal. No load test or bound is specified.  
   **Should say:** Define maximum concurrent requests and expected peak memory, then verify both with slow-upload and slow-download tests.

5. **Low — [decision-log.md:366](decision-log.md:366)**  
   **Claim:** “80% is the industry convention.”  
   **Problem:** Unsupported and irrelevant. A project-wide number can be met while the streaming/error paths remain untested.  
   **Should say:** “The chosen threshold is a repository gate; critical multipart, boundary, cancellation and streaming branches require explicit tests regardless of aggregate coverage.”

## 3. Design problems

1. **High — [epic.md:63](epic.md:63)**  
   **Claim:** Ports are declared by the consuming layer, yet `IFileRepository` and the mutation contract live in Domain while Application consumes them.  
   **Problem:** The rationale contradicts the placement. These are application orchestration ports unless they represent domain behaviour independent of this use case.  
   **Should say:** Put application-owned ports in Application; keep only the suffix-formatting policy/value types in Domain.

2. **High — [epic.md:77](epic.md:77), [architecture.md:141](architecture.md:141)**  
   **Claim:** Application owns text-decodability validation, but Infrastructure performs incremental UTF-8 validation while copying output.  
   **Problem:** The boundary is unresolved, and the Infrastructure placement creates the late-failure problem.  
   **Should say:** Assign encoding validation to a pre-response validation stage, or remove it. Do not describe it as Application validation while implementing it inside the output adapter.

3. **Medium — [file-mutation-api.md:79](file-mutation-api.md:79)**  
   **Claim:** `IFileMutatorRegistry` resolves filename and content type to the single mutator after the endpoint already validates both.  
   **Problem:** The same policy exists twice, and HTTP metadata leaks into the domain port. A one-entry registry prevents no bug that a direct `PlainTextMutator` plus one validator would not prevent.  
   **Should say:** Use one mutator and one acceptance-policy function. Introduce dispatch only when a second supported format exists.

4. **Medium — [ADR-0009:56](0009-accepted-formats-and-mutator-dispatch.md:56)**  
   **Claim:** `MutationCapability` exists “so a future adapter cannot lie.”  
   **Problem:** An enum property is self-reported metadata. Nothing prevents a buffering implementation from declaring `Streaming`, and nothing in the type system enforces its execution model.  
   **Should say:** Remove it until needed, or use distinct contracts such as `IStreamingMutator` and `IBufferedMutator` with different inputs and orchestration.

5. **High — [architecture.md:213](architecture.md:213), [architecture.md:54](architecture.md:54)**  
   **Claim:** `IFileRepository` is genuinely optional, but the diagram has `MutateFileUseCase -> IFileRepository` and DI wiring for a deferred adapter.  
   **Problem:** If it is a constructor dependency, the application cannot start without the adapter. If it is unused, the port is speculative.  
   **Should say:** Remove repository and local-disk storage entirely. Add persistence only if OQ-1 changes the functional requirement.

6. **Medium — [ADR-0009:31](0009-accepted-formats-and-mutator-dispatch.md:31)**  
   **Claim:** Only `.txt` + `text/plain` + UTF-8 is an “honest” interpretation of text file.  
   **Problem:** Extension and declared part content type are caller-controlled labels. `.log`, `.md`, extensionless UTF-8, CSV and JSON are all text. Rejecting them may be a sensible v1 scope, but it is invented product policy, not validation truth. UTF-16 is likewise a scope choice, not inherently corrupt if the suffix is encoded accordingly.  
   **Should say:** “Pending clarification, v1 supports UTF-8 `.txt` parts declared `text/plain`; this is a deliberately narrow compatibility policy.”

7. **Low — [decision-log.md:300](decision-log.md:300)**  
   **Claim:** The filename is sanitised while “preserving the original for the response.”  
   **Problem:** Those are different promises. A multipart filename may contain client paths, separators, control characters or an empty basename.  
   **Should say:** “Return the safe basename derived from the submitted filename; it may differ from the raw multipart value.”

8. **Low — [architecture.md:93](architecture.md:93)**  
   **Claim:** No aggregate root.  
   **Assessment:** The absence is correct; the problem is continuing to call a `FileName` plus a suffix formatter a DDD model.  
   **Should say:** “This use case has no aggregate or meaningful entity model. It uses dependency inversion and value validation, not substantive DDD.”

## 4. Missing considerations

1. **High — [file-mutation-api.md:86](file-mutation-api.md:86)**  
   **Claim:** “Fully asynchronous I/O end to end.”  
   **Missing:** No cancellation design.  
   **Should say:** Pass `HttpContext.RequestAborted` through multipart reads, decoder operations, `FlushAsync`, pipe completion and any temporary-file I/O. Client cancellation should not be converted into a 500.

2. **High — [file-mutation-api.md:70](file-mutation-api.md:70)**  
   **Claim:** Accept a single multipart file.  
   **Missing:** No parser strategy or policy for duplicate file fields, extra files, extra form fields, absent part content type, quoted/oversized boundaries, excessive part headers, `filename*`, or malformed final boundaries. Using `HttpRequest.Form`/`IFormFile` buffers uploaded files by default, contradicting the streaming claim. [Microsoft upload documentation](https://learn.microsoft.com/aspnet/core/mvc/models/file-uploads)  
   **Should say:** Use a sequential `MultipartReader`, impose boundary/header/part-count limits, accept exactly one named file part, and define how all extras are handled.

3. **High — [architecture.md:184](architecture.md:184)**  
   **Claim:** Read/write faults transition to “500 ProblemDetails.”  
   **Missing:** No distinction between faults before and after response start, nor cleanup rules.  
   **Should say:** Before headers, return `ProblemDetails`; afterward abort the response, log the failure, complete both pipe ends in `finally`, and never attempt a second response.

4. **Medium — [architecture.md:152](architecture.md:152)**  
   **Claim:** Return “200 + Content-Disposition.”  
   **Missing:** The transformed response length is generally unknown while streaming.  
   **Should say:** State that `Content-Length` is omitted and HTTP/1.1 chunked transfer or HTTP/2/3 framing is expected; test browser and proxy behaviour for unknown-length downloads.

5. **Medium — [file-mutation-api.md:85](file-mutation-api.md:85)**  
   **Claim:** Support tens of concurrent uploads.  
   **Missing:** Slow uploaders and slow downloaders can retain a request, decoder state and pooled buffers indefinitely. No timeout, data-rate, concurrency or overload response is defined.  
   **Should say:** Define request timeout/data-rate behaviour, bounded concurrency, and whether overload returns 429 or 503.

6. **Medium — [file-mutation-api.md:71](file-mutation-api.md:71)**  
   **Claim:** Append the UTC date and a random character sequence.  
   **Missing:** Exact byte contract: separator/newline, date versus timestamp, invariant format, single captured time, sequence length/alphabet, required entropy/collision behaviour, and BOM handling.  
   **Should say:** Define an exact suffix such as `\n2026-09-22:<N ASCII characters>` and test it byte-for-byte.

## 5. Over-engineering

1. **High — [epic.md:220](epic.md:220)**  
   **Claim:** Approximately 15.5 hours, 14 tasks and multiple PR waves for a “couple of hours” assignment.  
   **Problem:** This misses the assignment’s explicit time constraint by roughly an order of magnitude. Parallel agents do not make the delivered engineering effort proportionate.  
   **Should say:** One implementation increment, one test increment, and a short decision note; timebox optional optimization work.

2. **High — [epic.md:83](epic.md:83)**  
   **Claim:** Four production projects, five test projects, an architecture-test project and a benchmark project.  
   **Problem:** For one endpoint and one transformation, this primarily demonstrates ceremony and makes the actual streaming correctness harder to see.  
   **Should say:** One API project plus one test project is sufficient. Extract a small core library only if it materially improves testing.

3. **Medium — [epic.md:35](epic.md:35)**  
   **Claim:** Evaluate three mediator products and implement a second CQRS variant on a branch.  
   **Problem:** One command with no independent read model has no CQRS problem. Building a discarded variant is effort spent proving an obvious negative.  
   **Should say:** One sentence in the README/ADR: “CQRS/mediator was considered and rejected because there is one synchronous command and no useful pipeline behaviour.”

4. **Medium — [epic.md:33](epic.md:33), [epic.md:34](epic.md:34)**  
   **Claim:** Ship repository, registry and capability seams for hypothetical persistence and formats.  
   **Problem:** These are extensions to requirements explicitly declared out of scope. They add code and tests without protecting the current behaviour.  
   **Should say:** Implement the single UTF-8 transformation directly. Add seams when a second concrete requirement arrives.

The minimum credible redesign is: sequential multipart parsing with explicit limits, validate/spool before committing the response, one mutator, no repository or registry, direct `ProblemDetails` for expected failures, cancellation throughout, and one native-published smoke test if AOT remains in scope.
tokens used
124.974
The design is not implementation-ready. Its core contract is internally inconsistent: it streams unvalidated bytes to the response, then claims it can replace that response with a 415 `ProblemDetails` if invalid UTF-8 is discovered later. It cannot.

## 1. Technical errors

1. **High — [decision-log.md:103](decision-log.md:103), [architecture.md:99](architecture.md:99)**  
   **Claim:** “`MaxRequestBodySize` plus `MultipartBodyLengthLimit` — before the body is read”; a downstream length check is “dead code.”  
   **Wrong:** `MultipartBodyLengthLimit` is enforced while multipart sections are parsed; it can throw after reading has begun. Kestrel also cannot pre-reject a chunked request whose size is not known. Moreover, Kestrel limits the whole request, while the multipart limit applies to a part; setting both to the nominal file limit rejects a valid maximum-size file because of multipart overhead. [Microsoft’s upload documentation](https://learn.microsoft.com/aspnet/core/mvc/models/file-uploads) explicitly describes parsing-time enforcement.  
   **Should say:** “Configure a whole-request ceiling and independently count the selected part’s bytes while parsing. Some oversized requests can be rejected from `Content-Length`; others fail during reading. Test chunked input and a file exactly at `MaxFileBytes`.”

2. **High — [architecture.md:140](architecture.md:140)**  
   **Claim:** Copy each segment to the response `PipeWriter`, then, on invalid UTF-8, return “415 ProblemDetails.”  
   **Wrong:** Once output is flushed, headers/status may already be committed. Exception handling cannot re-execute or replace a started response. [ASP.NET Core documents this limitation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0). The client instead receives a truncated 200 or a reset connection.  
   **Should say:** Choose one:

   - Validate/spool first, then send a successful response.
   - Stop validating UTF-8 and treat the upload as opaque bytes.
   - Accept non-atomic streaming and document that failures after response start abort the body and cannot return `ProblemDetails`.

3. **High — [architecture.md:123](architecture.md:123), [architecture.md:163](architecture.md:163)**  
   **Claim:** Validation returns a result rather than throwing, but validation failures go through `IExceptionHandler`.  
   **Wrong:** `IExceptionHandler` handles exceptions raised through exception-handler middleware. It is not a dispatcher for result-based validation failures.  
   **Should say:** “Expected validation failures directly execute/return `TypedResults.Problem`; `IExceptionHandler` handles unexpected exceptions only, and only before the response starts.”

4. **High — [architecture.md:138](architecture.md:138), [epic.md:32](epic.md:32)**  
   **Claim:** Await `Mutate(PipeReader, PipeWriter, …)`, then return `Results.File`.  
   **Wrong:** Pipe ownership and execution are unspecified. If this is an internal bounded `Pipe`, awaiting the producer before returning the result can deadlock on backpressure because no consumer is executing yet. If it is `Response.BodyWriter`, the response starts during mutation and `Results.File` is no longer the response mechanism. .NET 10 has `Results.Stream(PipeReader, …, fileDownloadName:)`, but its producer must run concurrently and complete/fault the pipe correctly. [Minimal API response documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-10.0)  
   **Should say:** Specify whether the writer is the HTTP response or an internal pipe, who starts the producer, who completes both ends, and how producer faults are observed.

5. **Medium — [file-mutation-api.md:93](file-mutation-api.md:93)**  
   **Claim:** “Library projects set `IsAotCompatible` so violations fail at build.”  
   **Wrong:** `IsAotCompatible` enables analyzers; their diagnostics are warnings unless warnings are promoted to errors. RDG diagnostics likewise do not inherently stop the build. [Native AOT documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot)  
   **Should say:** “Enable AOT/trim analyzers and explicitly fail CI on relevant IL/RDG warnings; publish and run the native artifact.”

6. **Medium — [epic.md:46](epic.md:46)**  
   **Claim:** “AOT demands reflection-free, trim-safe code; so does a zero-allocation hot path … AOT analyzers catch violations.”  
   **Wrong:** Allocation behaviour and AOT compatibility are independent. AOT analyzers diagnose dynamic-code/trimming hazards, not managed allocations. Reflection-free code can allocate heavily; allocation-free code can be trimming-unsafe.  
   **Should say:** “AOT compatibility and allocation are separate constraints, verified by separate tests.”

7. **Medium — [epic.md:28](epic.md:28)**  
   **Claim:** “Reflection-based serialisation does not survive trimming.”  
   **Wrong:** Too categorical. Reflection-based `System.Text.Json` is disabled by default for trimmed publication because it is unsafe/unpredictable, not because all reflection serialization is physically impossible. Source generation is the correct choice, but the explanation is inaccurate. [System.Text.Json source-generation documentation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)  
   **Should say:** “Use generated metadata for every application JSON type because general reflection metadata cannot be relied upon after trimming/AOT.”

8. **Medium — [ADR-0009:56](0009-accepted-formats-and-mutator-dispatch.md:56)**  
   **Claim:** A `.docx` “must buffer the whole archive” because the ZIP central directory is at the end.  
   **Wrong:** `ZipArchiveMode.Update` holds the archive in memory, but that is an implementation limitation, not a ZIP-law requirement. An archive can be rewritten into a new output sequentially using seekable temporary storage or a suitable streaming reader/writer. `ZipArchiveMode.Create` itself supports non-seekable output. [ZipArchive documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchive.-ctor?view=net-10.0)  
   **Should say:** “The selected .NET/OpenXML approach would require seekable storage or buffering and is disproportionate; `.docx` is also outside the text-file requirement.”

9. **Medium — [epic.md:24](epic.md:24)**  
   **Claim:** ArchUnitNET mechanically enforces that Domain/Application have no compile-time dependency on outer projects.  
   **Wrong:** ArchUnitNET primarily analyzes type/member dependencies in compiled assemblies. An unused forbidden `ProjectReference` need not create such a dependency. [ArchUnitNET describes its bytecode/type analysis](https://github.com/TNG/ArchUnitNET).  
   **Should say:** “Use ArchUnitNET for type/namespace dependency rules and a separate project-reference/MSBuild check for the `.csproj` graph.”

10. **Medium — [file-mutation-api.md:53](file-mutation-api.md:53)**  
    **Claim:** “All error responses are RFC 7807 `ProblemDetails`.”  
    **Wrong:** Kestrel-level rejection, malformed HTTP, disconnects, cancellation, and failures after response start are not under that guarantee.  
    **Should say:** “Application-generated errors before response start use `ProblemDetails`; transport failures and mid-stream failures may terminate the connection without a structured body.”

## 2. Unjustified claims

1. **High — [decision-log.md:278](decision-log.md:278)**  
   **Claim:** BenchmarkDotNet makes “no per-request allocation that scales with file size” checkable.  
   **Problem:** No benchmark boundary, input-size sweep, warm-up state, or concurrency model is defined. A mutator-only benchmark omits multipart parsing, headers, error objects, Kestrel and pipe creation. `MemoryDiagnoser` also does not establish bounded retained memory or absence of LOH pool growth under concurrency.  
   **Should say:** “Measure allocated bytes for several file sizes and report the slope; separately run an end-to-end concurrent memory test and state exactly what is excluded.”

2. **Medium — [epic.md:43](epic.md:43)**  
   **Claim:** Native AOT makes first-request latency predictable, lowers RSS materially, and makes scale-to-zero viable.  
   **Problem:** There is no deployment target, baseline, cold-start budget, instance count or JIT comparison. These are plausible benefits, not established requirements.  
   **Should say:** “AOT is an experiment. Keep it only if measurements against framework-dependent JIT deployment justify its package and build constraints.”

3. **Medium — [epic.md:55](epic.md:55)**  
   **Claim:** Task 000 settles Scalar/AOT compatibility “by publishing.”  
   **Problem:** Publishing proves only compilation. The native binary must start, serve the OpenAPI document, render Scalar, show a multipart file control, submit a file, and download the response. `Microsoft.AspNetCore.OpenApi` is officially Native-AOT compatible, but the visual UI remains a third-party integration. [Microsoft OpenAPI documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0)  
   **Should say:** “Run a smoke test against the published native executable, with zero AOT warnings, including the browser upload/download path.”

4. **Medium — [file-mutation-api.md:85](file-mutation-api.md:85)**  
   **Claim:** Pipelines imply “tens of concurrent uploads without unbounded memory growth.”  
   **Problem:** Pipelines provide mechanisms for pooling and backpressure; correct bounds still depend on pause/resume thresholds, `AdvanceTo`, slow clients, concurrency limits and disposal. No load test or bound is specified.  
   **Should say:** Define maximum concurrent requests and expected peak memory, then verify both with slow-upload and slow-download tests.

5. **Low — [decision-log.md:366](decision-log.md:366)**  
   **Claim:** “80% is the industry convention.”  
   **Problem:** Unsupported and irrelevant. A project-wide number can be met while the streaming/error paths remain untested.  
   **Should say:** “The chosen threshold is a repository gate; critical multipart, boundary, cancellation and streaming branches require explicit tests regardless of aggregate coverage.”

## 3. Design problems

1. **High — [epic.md:63](epic.md:63)**  
   **Claim:** Ports are declared by the consuming layer, yet `IFileRepository` and the mutation contract live in Domain while Application consumes them.  
   **Problem:** The rationale contradicts the placement. These are application orchestration ports unless they represent domain behaviour independent of this use case.  
   **Should say:** Put application-owned ports in Application; keep only the suffix-formatting policy/value types in Domain.

2. **High — [epic.md:77](epic.md:77), [architecture.md:141](architecture.md:141)**  
   **Claim:** Application owns text-decodability validation, but Infrastructure performs incremental UTF-8 validation while copying output.  
   **Problem:** The boundary is unresolved, and the Infrastructure placement creates the late-failure problem.  
   **Should say:** Assign encoding validation to a pre-response validation stage, or remove it. Do not describe it as Application validation while implementing it inside the output adapter.

3. **Medium — [file-mutation-api.md:79](file-mutation-api.md:79)**  
   **Claim:** `IFileMutatorRegistry` resolves filename and content type to the single mutator after the endpoint already validates both.  
   **Problem:** The same policy exists twice, and HTTP metadata leaks into the domain port. A one-entry registry prevents no bug that a direct `PlainTextMutator` plus one validator would not prevent.  
   **Should say:** Use one mutator and one acceptance-policy function. Introduce dispatch only when a second supported format exists.

4. **Medium — [ADR-0009:56](0009-accepted-formats-and-mutator-dispatch.md:56)**  
   **Claim:** `MutationCapability` exists “so a future adapter cannot lie.”  
   **Problem:** An enum property is self-reported metadata. Nothing prevents a buffering implementation from declaring `Streaming`, and nothing in the type system enforces its execution model.  
   **Should say:** Remove it until needed, or use distinct contracts such as `IStreamingMutator` and `IBufferedMutator` with different inputs and orchestration.

5. **High — [architecture.md:213](architecture.md:213), [architecture.md:54](architecture.md:54)**  
   **Claim:** `IFileRepository` is genuinely optional, but the diagram has `MutateFileUseCase -> IFileRepository` and DI wiring for a deferred adapter.  
   **Problem:** If it is a constructor dependency, the application cannot start without the adapter. If it is unused, the port is speculative.  
   **Should say:** Remove repository and local-disk storage entirely. Add persistence only if OQ-1 changes the functional requirement.

6. **Medium — [ADR-0009:31](0009-accepted-formats-and-mutator-dispatch.md:31)**  
   **Claim:** Only `.txt` + `text/plain` + UTF-8 is an “honest” interpretation of text file.  
   **Problem:** Extension and declared part content type are caller-controlled labels. `.log`, `.md`, extensionless UTF-8, CSV and JSON are all text. Rejecting them may be a sensible v1 scope, but it is invented product policy, not validation truth. UTF-16 is likewise a scope choice, not inherently corrupt if the suffix is encoded accordingly.  
   **Should say:** “Pending clarification, v1 supports UTF-8 `.txt` parts declared `text/plain`; this is a deliberately narrow compatibility policy.”

7. **Low — [decision-log.md:300](decision-log.md:300)**  
   **Claim:** The filename is sanitised while “preserving the original for the response.”  
   **Problem:** Those are different promises. A multipart filename may contain client paths, separators, control characters or an empty basename.  
   **Should say:** “Return the safe basename derived from the submitted filename; it may differ from the raw multipart value.”

8. **Low — [architecture.md:93](architecture.md:93)**  
   **Claim:** No aggregate root.  
   **Assessment:** The absence is correct; the problem is continuing to call a `FileName` plus a suffix formatter a DDD model.  
   **Should say:** “This use case has no aggregate or meaningful entity model. It uses dependency inversion and value validation, not substantive DDD.”

## 4. Missing considerations

1. **High — [file-mutation-api.md:86](file-mutation-api.md:86)**  
   **Claim:** “Fully asynchronous I/O end to end.”  
   **Missing:** No cancellation design.  
   **Should say:** Pass `HttpContext.RequestAborted` through multipart reads, decoder operations, `FlushAsync`, pipe completion and any temporary-file I/O. Client cancellation should not be converted into a 500.

2. **High — [file-mutation-api.md:70](file-mutation-api.md:70)**  
   **Claim:** Accept a single multipart file.  
   **Missing:** No parser strategy or policy for duplicate file fields, extra files, extra form fields, absent part content type, quoted/oversized boundaries, excessive part headers, `filename*`, or malformed final boundaries. Using `HttpRequest.Form`/`IFormFile` buffers uploaded files by default, contradicting the streaming claim. [Microsoft upload documentation](https://learn.microsoft.com/aspnet/core/mvc/models/file-uploads)  
   **Should say:** Use a sequential `MultipartReader`, impose boundary/header/part-count limits, accept exactly one named file part, and define how all extras are handled.

3. **High — [architecture.md:184](architecture.md:184)**  
   **Claim:** Read/write faults transition to “500 ProblemDetails.”  
   **Missing:** No distinction between faults before and after response start, nor cleanup rules.  
   **Should say:** Before headers, return `ProblemDetails`; afterward abort the response, log the failure, complete both pipe ends in `finally`, and never attempt a second response.

4. **Medium — [architecture.md:152](architecture.md:152)**  
   **Claim:** Return “200 + Content-Disposition.”  
   **Missing:** The transformed response length is generally unknown while streaming.  
   **Should say:** State that `Content-Length` is omitted and HTTP/1.1 chunked transfer or HTTP/2/3 framing is expected; test browser and proxy behaviour for unknown-length downloads.

5. **Medium — [file-mutation-api.md:85](file-mutation-api.md:85)**  
   **Claim:** Support tens of concurrent uploads.  
   **Missing:** Slow uploaders and slow downloaders can retain a request, decoder state and pooled buffers indefinitely. No timeout, data-rate, concurrency or overload response is defined.  
   **Should say:** Define request timeout/data-rate behaviour, bounded concurrency, and whether overload returns 429 or 503.

6. **Medium — [file-mutation-api.md:71](file-mutation-api.md:71)**  
   **Claim:** Append the UTC date and a random character sequence.  
   **Missing:** Exact byte contract: separator/newline, date versus timestamp, invariant format, single captured time, sequence length/alphabet, required entropy/collision behaviour, and BOM handling.  
   **Should say:** Define an exact suffix such as `\n2026-09-22:<N ASCII characters>` and test it byte-for-byte.

## 5. Over-engineering

1. **High — [epic.md:220](epic.md:220)**  
   **Claim:** Approximately 15.5 hours, 14 tasks and multiple PR waves for a “couple of hours” assignment.  
   **Problem:** This misses the assignment’s explicit time constraint by roughly an order of magnitude. Parallel agents do not make the delivered engineering effort proportionate.  
   **Should say:** One implementation increment, one test increment, and a short decision note; timebox optional optimization work.

2. **High — [epic.md:83](epic.md:83)**  
   **Claim:** Four production projects, five test projects, an architecture-test project and a benchmark project.  
   **Problem:** For one endpoint and one transformation, this primarily demonstrates ceremony and makes the actual streaming correctness harder to see.  
   **Should say:** One API project plus one test project is sufficient. Extract a small core library only if it materially improves testing.

3. **Medium — [epic.md:35](epic.md:35)**  
   **Claim:** Evaluate three mediator products and implement a second CQRS variant on a branch.  
   **Problem:** One command with no independent read model has no CQRS problem. Building a discarded variant is effort spent proving an obvious negative.  
   **Should say:** One sentence in the README/ADR: “CQRS/mediator was considered and rejected because there is one synchronous command and no useful pipeline behaviour.”

4. **Medium — [epic.md:33](epic.md:33), [epic.md:34](epic.md:34)**  
   **Claim:** Ship repository, registry and capability seams for hypothetical persistence and formats.  
   **Problem:** These are extensions to requirements explicitly declared out of scope. They add code and tests without protecting the current behaviour.  
   **Should say:** Implement the single UTF-8 transformation directly. Add seams when a second concrete requirement arrives.

The minimum credible redesign is: sequential multipart parsing with explicit limits, validate/spool before committing the response, one mutator, no repository or registry, direct `ProblemDetails` for expected failures, cancellation throughout, and one native-published smoke test if AOT remains in scope.

# Allocation benchmark results

Command run from the repository root:

```text
env NUGET_PACKAGES=/private/tmp/file-mutation-nuget/packages NUGET_HTTP_CACHE_PATH=/private/tmp/file-mutation-nuget/http-cache dotnet run -c Release --project benchmarks/FileMutation.Benchmarks
```

BenchmarkDotNet host and runtime report:

```text
BenchmarkDotNet v0.15.8, macOS 27.2 (26B5091g) [Darwin 27.2.0]
Unknown processor
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.9 (10.0.9, 10.0.926.27113), Arm64 RyuJIT armv8.0-a
```

BenchmarkDotNet summary:

| Method                 | FileSizeBytes | Mean         | Error        | StdDev       | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated  | Alloc Ratio |
|----------------------- |-------------- |-------------:|-------------:|-------------:|------:|--------:|---------:|---------:|---------:|-----------:|------------:|
| **NaiveReadAllBytesAsync** | **1024**          |     **117.3 ns** |      **1.04 ns** |      **0.97 ns** |  **1.00** |    **0.01** |   **0.2620** |        **-** |        **-** |     **2192 B** |        **1.00** |
| PipelinesMutationAsync | 1024          |     302.4 ns |      6.04 ns |     11.05 ns |  2.58 |    0.10 |   0.0935 |        - |        - |      784 B |        0.36 |
|                        |               |              |              |              |       |         |          |          |          |            |             |
| **NaiveReadAllBytesAsync** | **262144**        |  **54,568.3 ns** |  **1,078.18 ns** |  **1,475.82 ns** |  **1.00** |    **0.04** | **466.5527** | **466.5527** | **133.3008** |   **525017 B** |       **1.000** |
| PipelinesMutationAsync | 262144        |   6,792.6 ns |    133.06 ns |    142.37 ns |  0.12 |    0.00 |   0.0916 |        - |        - |      784 B |       0.001 |
|                        |               |              |              |              |       |         |          |          |          |            |             |
| **NaiveReadAllBytesAsync** | **10485760**      | **918,284.6 ns** | **14,775.47 ns** | **13,820.98 ns** |  **1.00** |    **0.02** | **672.8516** | **671.8750** | **666.9922** | **20971880 B** |       **1.000** |
| PipelinesMutationAsync | 10485760      | 279,895.9 ns |  5,516.58 ns |  6,567.10 ns |  0.30 |    0.01 |        - |        - |        - |      792 B |       0.000 |

The Pipelines mutation path allocated 784 B per operation for 1 KB and 256 KB inputs, and 792 B at 10 MB; its allocation does not scale with input size in this run. The naive path allocated 2,192 B at 1 KB, 525,017 B at 256 KB, and 20,971,880 B at 10 MB because it materializes both input and concatenated output arrays. No Pipelines row recorded Gen1 or Gen2 collections, while the naive 256 KB and 10 MB rows recorded Gen2 collections. The baseline's arrays above 85,000 bytes are large-object-heap allocations; no comparable LOH-sized managed allocation appears in the Pipelines rows.

## The trade-off the table also shows

At **1 KB the Pipelines path is 2.58× slower** than the naive one — 302 ns against 117 ns — and
allocates 784 B against 2,192 B. For a file that small, the pipe machinery costs more than the
copy it avoids. The picture inverts with size: at 256 KB Pipelines is 8× faster, and at 10 MB it
is 3.3× faster while allocating 26,000× less.

This is the honest shape of the decision. The design is chosen for uploads at the configured
ceiling, where the naive path allocates a 10 MB array on the large object heap per request and
collects it in Gen2. A service that only ever received 1 KB files would be better off with the
naive implementation, and the benchmark is in the repository so that claim can be re-checked
rather than argued.

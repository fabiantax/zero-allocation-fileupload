using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FileMutation.Domain;
using FileMutation.Domain.Ports;
using FileMutation.Infrastructure;

BenchmarkRunner.Run<MutationBenchmarks>();

/// <summary>Measures streamed mutation allocations against a whole-buffer baseline.</summary>
[MemoryDiagnoser]
public class MutationBenchmarks
{
    private const int SuffixLength = 28;
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
    private static readonly IRandomSequenceGenerator RandomSequenceGenerator = new FixedRandomSequenceGenerator();
    private static readonly DateAndRandomSequenceMutator Mutator = new(new FixedTimeProvider(), RandomSequenceGenerator);
    private byte[] _source = [];

    /// <summary>Gets or sets the number of source bytes for the current benchmark case.</summary>
    [Params(1 * 1024, 256 * 1024, 10 * 1024 * 1024)]
    public int FileSizeBytes { get; set; }

    /// <summary>Creates the reusable source data outside the measured operation.</summary>
    [GlobalSetup]
    public void CreateSource()
    {
        _source = new byte[FileSizeBytes];
        _source.AsSpan().Fill((byte)'x');
    }

    /// <summary>Measures a whole-buffer read, append, and write operation.</summary>
    [Benchmark(Baseline = true)]
    public async Task NaiveReadAllBytesAsync()
    {
        await using var source = new MemoryStream(_source, writable: false);
        var input = new byte[source.Length];
        await source.ReadExactlyAsync(input);

        var output = new byte[input.Length + SuffixLength];
        input.CopyTo(output, 0);
        AppendSuffix(output.AsSpan(input.Length));
        await Stream.Null.WriteAsync(output);
    }

    /// <summary>Measures the existing Pipelines-based mutator.</summary>
    [Benchmark]
    public async Task PipelinesMutationAsync()
    {
        await using var source = new MemoryStream(_source, writable: false);
        await Mutator.MutateAsync(source, Stream.Null);
    }

    private static void AppendSuffix(Span<byte> destination)
    {
        Span<char> randomSequence = stackalloc char[DateAndRandomSequenceMutator.RandomSequenceLength];
        RandomSequenceGenerator.Fill(randomSequence);
        var context = new MutationContext(Timestamp, randomSequence);
        _ = MutationPolicy.TryWriteSuffix(destination, in context, out _);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Timestamp;
    }

    private sealed class FixedRandomSequenceGenerator : IRandomSequenceGenerator
    {
        public void Fill(Span<char> destination) => "0123456789ABCDEF".AsSpan().CopyTo(destination);
    }
}

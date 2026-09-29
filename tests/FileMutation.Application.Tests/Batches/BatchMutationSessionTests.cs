using System.Runtime.CompilerServices;
using System.Text;
using FileMutation.Application.Batches;
using FileMutation.Domain.Batches;
using FileMutation.Domain.Events;
using FileMutation.Domain.Ports;
using FileMutation.TestCommon;
using Xunit;

namespace FileMutation.Application.Tests.Batches;

public sealed class BatchMutationSessionTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private readonly RecordingPublisher _published = new();

    [Fact]
    public async Task Three_files_give_mutated_rejected_mutated_and_the_exact_event_sequence()
    {
        var session = Start();
        var contents = new List<string>();
        foreach (var (name, text) in new[] { ("a.txt", "one"), ("b.json", "two"), ("c.txt", "three") })
        {
            await using var result = await session.MutateNextAsync(Bytes(text), name, "text/plain", default);
            if (result.Outcome.Status == BatchFileStatus.Mutated)
            {
                contents.Add(await new StreamReader(result.Content).ReadToEndAsync());
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => result.Content);
            }
        }

        var id = session.Id;
        Assert.Equal(["one-mutated", "three-mutated"], contents);
        Assert.Equal<BatchFileOutcome>(
            [
                new(0, "a.txt", "a.txt", BatchFileStatus.Mutated, null),
                new(1, "b.json", null, BatchFileStatus.Rejected, "UnsupportedFileExtension"),
                new(2, "c.txt", "c.txt", BatchFileStatus.Mutated, null)
            ],
            session.Outcomes);
        Assert.Equal<BatchEvent>(
            [
                new FileMutated(id, 1, At, 0, "a.txt"),
                new FileRejected(id, 2, At, 1, "b.json", "UnsupportedFileExtension"),
                new FileMutated(id, 3, At, 2, "c.txt")
            ],
            _published.Events);
    }

    [Fact]
    public async Task Duplicate_entry_names_are_numbered_ignoring_case_and_never_issued_twice()
    {
        var session = Start();
        var entryNames = new List<string?>();
        foreach (var name in new[] { "a.txt", "a.txt", "A.TXT", "a (2).txt" })
        {
            entryNames.Add((await SendAsync(session, name)).EntryName);
        }

        Assert.Equal(["a.txt", "a (2).txt", "A (3).TXT", "a (2) (2).txt"], entryNames);
        Assert.Equal(entryNames, _published.Events.OfType<FileMutated>().Select(e => e.EntryName));
    }

    [Fact]
    public async Task The_file_after_max_files_is_rejected_without_its_content_being_read()
    {
        var session = Start(maxFiles: 2);
        await SendAsync(session, "a.txt");
        await SendAsync(session, "b.txt");
        var overflow = new TrapStream();

        await using var result = await session.MutateNextAsync(overflow, "c.txt", "text/plain", default);

        Assert.False(overflow.WasRead);
        Assert.Equal(new BatchFileOutcome(2, "c.txt", null, BatchFileStatus.Rejected, "BatchFileLimitExceeded"), result.Outcome);
        Assert.Equal(new FileRejected(session.Id, 3, At, 2, "c.txt", "BatchFileLimitExceeded"), _published.Events[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_mutation_that_throws_on_file_two_is_a_rejected_row_and_files_one_and_three_succeed(bool serviceThrows)
    {
        var single = new SingleFileMutationService(new SuffixMutator(failOnCall: serviceThrows ? 0 : 2));
        var session = Start(serviceThrows ? new ThrowOnSecondCall(single) : single);

        var outcomes = new List<BatchFileOutcome>();
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            outcomes.Add(await SendAsync(session, name));
        }

        Assert.Equal(
            [BatchFileStatus.Mutated, BatchFileStatus.Rejected, BatchFileStatus.Mutated],
            outcomes.Select(outcome => outcome.Status));
        Assert.Equal("MutationFailed", outcomes[1].ReasonCode);
        Assert.Equal(new FileRejected(session.Id, 2, At, 1, "b.txt", "MutationFailed"), _published.Events[1]);
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_becoming_a_rejected_row()
    {
        var files = new ThrowOnSecondCall(new SingleFileMutationService(new SuffixMutator()), new OperationCanceledException());
        var session = Start(files);
        await SendAsync(session, "a.txt");

        await Assert.ThrowsAsync<OperationCanceledException>(() => SendAsync(session, "b.txt"));

        Assert.Single(session.Outcomes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sequence_numbers_run_from_one_without_gaps_even_when_the_publisher_drops_events(bool accepted)
    {
        var publisher = new RecordingPublisher(accepted);
        var session = Start(publisher: publisher);
        foreach (var name in new[] { "a.txt", "b.json", "c.txt", "d.txt", "e.txt" })
        {
            await SendAsync(session, name);
        }

        Assert.Equal(Enumerable.Range(1, 5).Select(n => (long)n), publisher.Events.Select(e => e.Sequence));
        Assert.All(publisher.Events, e => Assert.Equal(session.Id, e.BatchId));
    }

    [Fact]
    public async Task Start_publishes_nothing_and_each_session_has_its_own_id_and_sequence()
    {
        var first = Start();
        var second = Start();
        Assert.Empty(_published.Events);

        await SendAsync(first, "a.txt");
        await SendAsync(second, "a.txt");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal([1L, 1L], _published.Events.Select(e => e.Sequence));
    }

    [Fact]
    public async Task The_session_keeps_neither_the_upload_nor_the_result_once_the_caller_disposes_it()
    {
        var session = Start();
        var (upload, result) = await SendAndForgetAsync(session);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(upload.IsAlive, "the session still references the uploaded stream");
        Assert.False(result.IsAlive, "the session still references a disposed result");
        Assert.Single(session.Outcomes);
    }

    [Theory]
    [InlineData(0, 10)]
    public void Start_rejects_limits_that_would_break_the_session(long maxFileBytes, int chunkSize) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Start(limits: new BatchLimits(5, maxFileBytes, chunkSize)));

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    // Not inlined, so the upload and the result are unreachable from the test once it returns.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Upload, WeakReference Result)> SendAndForgetAsync(BatchMutationSession session)
    {
        var upload = Bytes("x");
        var result = await session.MutateNextAsync(upload, "a.txt", "text/plain", default);
        await result.DisposeAsync();
        return (new WeakReference(upload), new WeakReference(result));
    }

    private static async Task<BatchFileOutcome> SendAsync(BatchMutationSession session, string name)
    {
        await using var result = await session.MutateNextAsync(Bytes("x"), name, "text/plain", default);
        return result.Outcome;
    }

    private BatchMutationSession Start(
        ISingleFileMutationService? files = null, int maxFiles = 100, int chunkSize = 10,
        RecordingPublisher? publisher = null, BatchLimits? limits = null) =>
        new BatchMutationService(
                files ?? new SingleFileMutationService(new SuffixMutator()), publisher ?? _published, new FixedTimeProvider(At))
            .Start(limits ?? new BatchLimits(maxFiles, 1024, chunkSize));

    private sealed class RecordingPublisher(bool accept = true) : IEventPublisher
    {
        public List<BatchEvent> Events { get; } = [];

        public bool TryPublish(BatchEvent batchEvent)
        {
            Events.Add(batchEvent);
            return accept;
        }
    }

    private sealed class SuffixMutator(int failOnCall = 0) : IFileMutator
    {
        private int _calls;

        public async ValueTask MutateAsync(Stream source, Stream destination, CancellationToken cancellationToken = default)
        {
            if (++_calls == failOnCall)
            {
                throw new IOException("mutation failed");
            }

            await source.CopyToAsync(destination, cancellationToken);
            await destination.WriteAsync("-mutated"u8.ToArray(), cancellationToken);
        }
    }

    private sealed class ThrowOnSecondCall(ISingleFileMutationService inner, Exception? fault = null) : ISingleFileMutationService
    {
        private int _calls;

        public Task<FileMutationResult> MutateAsync(
            Stream content, string? declaredFileName, string? declaredContentType, long maxFileBytes,
            CancellationToken cancellationToken = default) =>
            ++_calls == 2
                ? throw (fault ?? new InvalidOperationException("service failed"))
                : inner.MutateAsync(content, declaredFileName, declaredContentType, maxFileBytes, cancellationToken);
    }

    private sealed class TrapStream : MemoryStream
    {
        public bool WasRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count) => Trap();

        public override int Read(Span<byte> buffer) => Trap();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Trap());

        private int Trap()
        {
            WasRead = true;
            throw new InvalidOperationException("The content of a file past the limit must not be read.");
        }
    }
}

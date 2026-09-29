using System.Diagnostics.Metrics;
using FileMutation.Domain.Batches;
using FileMutation.Domain.Events;
using FileMutation.Domain.Ports;
using FileMutation.Infrastructure;
using FileMutation.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileMutation.Infrastructure.Tests;

public sealed class ChannelEventPublisherTests : IDisposable
{
    private const string DroppedCounter = "filemutation.events.dropped";

    private readonly BatchId _batch = BatchId.New();
    private readonly MeterListener _listener = new();
    private long _dropped;
    private ServiceProvider? _provider;

    public ChannelEventPublisherTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == DroppedCounter)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => Interlocked.Add(ref _dropped, measurement));
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _provider?.Dispose();
    }

    [Fact]
    public void Below_capacity_events_are_queued_in_publish_order()
    {
        var (publisher, stream) = Build(capacity: 8);
        var published = Enumerable.Range(1, 5).Select(n => Mutated(n)).ToArray();

        Assert.All(published, e => Assert.True(publisher.TryPublish(e)));

        Assert.Equal<BatchEvent>(published, Drain(stream));
    }

    [Fact]
    public void Terminal_events_keep_their_place_in_the_order()
    {
        var (publisher, stream) = Build(capacity: 8);
        BatchEvent[] published = [Mutated(1), Mutated(2), Completed(3)];

        Assert.All(published, e => Assert.True(publisher.TryPublish(e)));

        Assert.Equal(published, Drain(stream));
    }

    [Fact]
    public void At_capacity_a_non_terminal_event_is_dropped_and_counted()
    {
        var (publisher, stream) = Build(capacity: 2);
        Assert.True(publisher.TryPublish(Mutated(1)));
        Assert.True(publisher.TryPublish(Mutated(2)));

        Assert.False(publisher.TryPublish(Mutated(3)));
        Assert.False(publisher.TryPublish(Mutated(4)));

        Assert.Equal(2, Interlocked.Read(ref _dropped));
        Assert.Equal([1L, 2L], Drain(stream).Select(e => e.Sequence));
    }

    [Fact]
    public void At_capacity_a_terminal_event_is_still_queued_and_not_counted_as_dropped()
    {
        var (publisher, stream) = Build(capacity: 1);
        Assert.True(publisher.TryPublish(Mutated(1)));

        Assert.True(publisher.TryPublish(Completed(2)));

        Assert.Equal(0, Interlocked.Read(ref _dropped));
        Assert.Equal([1L, 2L], Drain(stream).Select(e => e.Sequence));
    }

    [Fact]
    public void Terminal_events_do_not_use_up_capacity()
    {
        var (publisher, _) = Build(capacity: 1);
        Assert.True(publisher.TryPublish(Completed(1)));
        Assert.True(publisher.TryPublish(Completed(2)));

        Assert.True(publisher.TryPublish(Mutated(3)));
    }

    [Fact]
    public void Reading_a_non_terminal_event_frees_a_slot()
    {
        var (publisher, stream) = Build(capacity: 1);
        Assert.True(publisher.TryPublish(Mutated(1)));
        Assert.False(publisher.TryPublish(Mutated(2)));

        Assert.True(stream.Reader.TryRead(out _));

        Assert.True(publisher.TryPublish(Mutated(3)));
    }

    [Fact]
    public void Reading_a_terminal_event_does_not_free_a_slot()
    {
        var (publisher, stream) = Build(capacity: 1);
        Assert.True(publisher.TryPublish(Completed(1)));
        Assert.True(publisher.TryPublish(Mutated(2)));

        Assert.True(stream.Reader.TryRead(out var terminal));

        Assert.True(terminal.IsTerminal);
        Assert.False(publisher.TryPublish(Mutated(3)));
    }

    [Fact]
    public void Concurrent_publishers_never_queue_more_than_capacity()
    {
        const int capacity = 16;
        const int publishers = 8;
        const int eventsEach = 200;
        var (publisher, stream) = Build(capacity);
        var accepted = 0;

        Parallel.For(0, publishers, new ParallelOptions { MaxDegreeOfParallelism = publishers }, worker =>
        {
            for (var n = 0; n < eventsEach; n++)
            {
                if (publisher.TryPublish(Mutated(worker * eventsEach + n)))
                {
                    Interlocked.Increment(ref accepted);
                }
            }
        });

        Assert.Equal(capacity, accepted);
        Assert.Equal(capacity, Drain(stream).Count);
        Assert.Equal(publishers * eventsEach - capacity, Interlocked.Read(ref _dropped));
    }

    [Fact]
    public void Concurrent_terminal_events_are_all_queued_and_never_counted_against_capacity()
    {
        const int publishers = 8;
        const int eventsEach = 50;
        var (publisher, stream) = Build(capacity: 4);

        Parallel.For(0, publishers, new ParallelOptions { MaxDegreeOfParallelism = publishers }, worker =>
        {
            for (var n = 0; n < eventsEach; n++)
            {
                Assert.True(publisher.TryPublish(Completed(worker * eventsEach + n)));
            }
        });

        Assert.Equal(publishers * eventsEach, Drain(stream).Count);
        Assert.Equal(0, Interlocked.Read(ref _dropped));
        Assert.True(publisher.TryPublish(Mutated(1)));
    }

    [Fact]
    public void The_publisher_and_stream_registrations_share_one_queue()
    {
        var (publisher, stream) = Build(capacity: 4);

        Assert.Same(publisher, _provider!.GetRequiredService<IEventPublisher>());
        Assert.Same(stream, _provider!.GetRequiredService<IEventStream>());
    }

    [Fact]
    public void The_default_capacity_is_1024_when_the_host_registers_no_options()
    {
        _provider = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddFileMutationInfrastructure()
            .BuildServiceProvider();
        var publisher = _provider.GetRequiredService<IEventPublisher>();

        var accepted = Enumerable.Range(1, 1_025).Count(n => publisher.TryPublish(Mutated(n)));

        Assert.Equal(1_024, accepted);
    }

    [Fact]
    public async Task WaitToReadAsync_completes_when_an_event_arrives()
    {
        var (publisher, stream) = Build(capacity: 4);
        var waiting = stream.Reader.WaitToReadAsync().AsTask();
        Assert.False(waiting.IsCompleted);

        publisher.TryPublish(Mutated(1));

        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_null_event_is_rejected()
    {
        var (publisher, _) = Build(capacity: 1);

        Assert.Throws<ArgumentNullException>(() => publisher.TryPublish(null!));
    }

    private (IEventPublisher Publisher, IEventStream Stream) Build(int capacity)
    {
        _provider = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddSingleton(new EventBusOptions { Capacity = capacity })
            .AddFileMutationInfrastructure()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        return (_provider.GetRequiredService<IEventPublisher>(), _provider.GetRequiredService<IEventStream>());
    }

    private BatchEvent Mutated(long sequence) =>
        new FileMutated(_batch, sequence, DateTimeOffset.UnixEpoch, 0, "a.txt");

    private BatchEvent Completed(long sequence) =>
        new BatchCompleted(_batch, sequence, DateTimeOffset.UnixEpoch, 1, 0);

    private static List<BatchEvent> Drain(IEventStream stream)
    {
        var drained = new List<BatchEvent>();
        while (stream.Reader.TryRead(out var batchEvent))
        {
            drained.Add(batchEvent);
        }

        return drained;
    }
}

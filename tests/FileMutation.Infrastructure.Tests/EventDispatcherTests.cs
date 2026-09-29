using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FileMutation.Domain.Batches;
using FileMutation.Domain.Events;
using FileMutation.Domain.Ports;
using FileMutation.Infrastructure;
using FileMutation.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FileMutation.Infrastructure.Tests;

public sealed class EventDispatcherTests : IAsyncLifetime
{
    private const string FailureCounter = "filemutation.events.handler_failures";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly BatchId _batch = BatchId.New();
    private readonly Probe _probe = new();
    private readonly CapturingLogger _logs = new();
    private readonly ConcurrentQueue<string?> _failedHandlers = new();
    private readonly MeterListener _listener = new();
    private IHost? _host;

    public EventDispatcherTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == FailureCounter)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "handler")
                {
                    _failedHandlers.Enqueue(tag.Value as string);
                }
            }
        });
        _listener.Start();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _listener.Dispose();
        if (_host is not null)
        {
            // Stopping cancels the token the slow and gated handlers wait on.
            await _host.StopAsync(Timeout);
            _host.Dispose();
        }
    }

    [Fact]
    public async Task A_throwing_handler_does_not_stop_the_next_handler_the_next_event_or_the_host()
    {
        var host = await StartAsync(s => s.AddEventHandler<FileMutated, Thrower>().AddEventHandler<FileMutated, Second>());

        Publish(Mutated(1), Mutated(2));
        await _probe.WaitForAsync(2);

        Assert.Equal(["Second:1", "Second:2"], _probe.Names());
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
    }

    [Fact]
    public async Task A_failing_handler_is_logged_with_its_exception_and_counted_once_per_failure()
    {
        await StartAsync(s => s.AddEventHandler<FileMutated, Thrower>().AddEventHandler<FileMutated, Second>());

        Publish(Mutated(1), Mutated(2));
        await _probe.WaitForAsync(2);

        Assert.Equal([nameof(Thrower), nameof(Thrower)], _failedHandlers);
        var errors = _logs.Entries.Where(entry => entry.Level == LogLevel.Error).ToList();
        Assert.Equal(2, errors.Count);
        Assert.All(errors, entry =>
        {
            Assert.IsType<InvalidOperationException>(entry.Exception);
            Assert.Contains(nameof(Thrower), entry.Message);
        });
    }

    [Fact]
    public async Task A_handler_that_throws_OperationCanceledException_on_its_own_is_a_failure_not_a_shutdown()
    {
        var host = await StartAsync(s => s.AddEventHandler<FileMutated, Canceller>().AddEventHandler<FileMutated, Second>());

        Publish(Mutated(1), Mutated(2));
        await _probe.WaitForAsync(2);

        Assert.Equal([nameof(Canceller), nameof(Canceller)], _failedHandlers);
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
    }

    [Fact]
    public async Task A_handler_that_awaits_five_seconds_does_not_delay_TryPublish()
    {
        await StartAsync(s => s.AddEventHandler<FileMutated, Slow>());
        Publish(Mutated(1));
        await _probe.WaitForAsync(1);

        var started = Stopwatch.GetTimestamp();
        var accepted = Publish(Mutated(2));
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.True(accepted);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(50), $"TryPublish took {elapsed.TotalMilliseconds:F1} ms while a handler was busy");
    }

    [Fact]
    public async Task Each_handler_receives_only_the_event_type_it_subscribed_to()
    {
        await StartAsync(s => s
            .AddEventHandler<FileMutated, First>()
            .AddEventHandler<FileRejected, Sibling>()
            .AddEventHandler<BatchEvent, BaseListener>()
            .AddEventHandler<BatchAborted, Sentinel>());

        Publish(Mutated(1), Rejected(2), new BatchChunkCompleted(_batch, 3, DateTimeOffset.UnixEpoch, 1, 1, 1), Aborted(4));
        await _probe.WaitForAsync(3);

        // Sentinel is registered last, so no wrongly routed delivery can arrive after it. A handler that was
        // called with the wrong type would fail at the cast, which shows up as a counted failure.
        Assert.Equal(["First:1", "Sibling:2", "Sentinel:4"], _probe.Names());
        Assert.Empty(_failedHandlers);
    }

    [Fact]
    public async Task Every_matching_subscription_runs_for_each_event_in_registration_order()
    {
        await StartAsync(s => s
            .AddEventHandler<FileMutated, First>()
            .AddEventHandler<FileMutated, Second>()
            .AddEventHandler<FileMutated, Third>());

        Publish(Mutated(1), Mutated(2));
        await _probe.WaitForAsync(6);

        Assert.Equal(["First:1", "Second:1", "Third:1", "First:2", "Second:2", "Third:2"], _probe.Names());
    }

    [Fact]
    public async Task One_handler_type_registered_for_five_event_types_is_one_instance_that_receives_all_five()
    {
        var host = await StartAsync(s => s
            .AddEventHandler<FileMutated, Everything>()
            .AddEventHandler<FileRejected, Everything>()
            .AddEventHandler<BatchChunkCompleted, Everything>()
            .AddEventHandler<BatchCompleted, Everything>()
            .AddEventHandler<BatchAborted, Everything>());

        Publish(Mutated(1), Rejected(2), new BatchChunkCompleted(_batch, 3, DateTimeOffset.UnixEpoch, 1, 1, 1), Completed(4), Aborted(5));
        await _probe.WaitForAsync(5);

        // GetServices resolves every registration of the type: two registrations would be two instances.
        Assert.Single(host.Services.GetServices<Everything>());
        Assert.Equal(1, _probe.Constructed);
        Assert.Equal(["Everything:1", "Everything:2", "Everything:3", "Everything:4", "Everything:5"], _probe.Names());
    }

    [Fact]
    public async Task Registering_handlers_repeatedly_starts_exactly_one_dispatcher()
    {
        var host = await StartAsync(s => s
            .AddEventHandler<FileMutated, First>()
            .AddEventHandler<FileRejected, Sibling>()
            .AddEventHandler<BatchAborted, Sentinel>());

        // The dispatcher is internal; it is the only hosted service in the Infrastructure assembly.
        var dispatchers = host.Services.GetServices<IHostedService>()
            .Where(service => service.GetType().Assembly == typeof(EventHandlerRegistration).Assembly);

        Assert.Single(dispatchers);
    }

    [Fact]
    public async Task Events_of_one_batch_reach_a_handler_in_sequence_order()
    {
        var otherBatch = BatchId.New();
        await StartAsync(s => s.AddEventHandler<FileMutated, First>());

        for (long sequence = 1; sequence <= 15; sequence++)
        {
            Publish(Mutated(sequence), Mutated(sequence, otherBatch));
        }

        await _probe.WaitForAsync(30);

        var perBatch = _probe.Calls.GroupBy(call => call.Event.BatchId).ToList();
        Assert.Equal(2, perBatch.Count);
        Assert.All(perBatch, group => Assert.Equal(Enumerable.Range(1, 15).Select(n => (long)n), group.Select(call => call.Event.Sequence)));
    }

    [Fact]
    public async Task Stopping_the_host_ends_the_dispatcher_cleanly_and_leaves_queued_events_unhandled()
    {
        var host = await StartAsync(s => s.AddEventHandler<FileMutated, Gated>());
        Publish(Mutated(1), Mutated(2));
        await _probe.WaitForAsync(1);

        var dispatcher = host.Services.GetServices<IHostedService>().OfType<BackgroundService>().Single();

        // Gated returns normally once the stop token fires, so only the loop itself can keep event 2 away.
        await host.StopAsync().WaitAsync(Timeout);

        Assert.Equal(["Gated:1"], _probe.Names());
        Assert.True(dispatcher.ExecuteTask!.IsCompletedSuccessfully, $"The dispatcher ended {dispatcher.ExecuteTask.Status}, not cleanly");
        Assert.DoesNotContain(_logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.Empty(_failedHandlers);
    }

    [Fact]
    public async Task A_handler_cancelled_by_the_host_stopping_is_not_reported_as_a_failure()
    {
        var host = await StartAsync(s => s.AddEventHandler<FileMutated, Cancelling>());
        Publish(Mutated(1));
        await _probe.WaitForAsync(1);

        await host.StopAsync().WaitAsync(Timeout);

        // Cancelling throws OperationCanceledException from the stop token; that is the shutdown, not a fault.
        Assert.DoesNotContain(_logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.Empty(_failedHandlers);
    }

    private IHost Build(Action<IServiceCollection> register)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(_probe);
        builder.Services.AddFileMutationInfrastructure();
        register(builder.Services);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        return _host = builder.Build();
    }

    private async Task<IHost> StartAsync(Action<IServiceCollection> register)
    {
        var host = Build(register);
        await host.StartAsync();
        return host;
    }

    private bool Publish(params BatchEvent[] events)
    {
        var publisher = _host!.Services.GetRequiredService<IEventPublisher>();
        return events.All(publisher.TryPublish);
    }

    private BatchEvent Mutated(long sequence, BatchId? batch = null) =>
        new FileMutated(batch ?? _batch, sequence, DateTimeOffset.UnixEpoch, 0, "a.txt");

    private BatchEvent Rejected(long sequence) =>
        new FileRejected(_batch, sequence, DateTimeOffset.UnixEpoch, 0, "b.json", "UnsupportedFileExtension");

    private BatchEvent Completed(long sequence) => new BatchCompleted(_batch, sequence, DateTimeOffset.UnixEpoch, 1, 0);

    private BatchEvent Aborted(long sequence) => new BatchAborted(_batch, sequence, DateTimeOffset.UnixEpoch, 0, "Disposed");

    private sealed class Probe
    {
        private readonly SemaphoreSlim _handled = new(0);
        private int _constructed;

        public ConcurrentQueue<(string Handler, BatchEvent Event)> Calls { get; } = new();

        public int Constructed => Volatile.Read(ref _constructed);

        public void Constructing() => Interlocked.Increment(ref _constructed);

        public string[] Names() => [.. Calls.Select(call => $"{call.Handler}:{call.Event.Sequence}")];

        // The yield makes every handler genuinely asynchronous, so a dispatcher that did not await it in order would show.
        public async ValueTask Record(string handler, BatchEvent batchEvent)
        {
            await Task.Yield();
            Calls.Enqueue((handler, batchEvent));
            _handled.Release();
        }

        public async Task WaitForAsync(int calls)
        {
            for (var n = 0; n < calls; n++)
            {
                Assert.True(await _handled.WaitAsync(Timeout), $"Only {n} of {calls} handler calls happened within {Timeout.TotalSeconds} s");
            }
        }
    }

    private sealed class CapturingLogger : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, exception, formatter(state, exception)));

        public void Dispose()
        {
        }
    }

    private sealed class First(Probe probe) : IEventHandler<FileMutated>
    {
        public ValueTask HandleAsync(FileMutated e, CancellationToken ct) => probe.Record(nameof(First), e);
    }

    private sealed class Second(Probe probe) : IEventHandler<FileMutated>
    {
        public ValueTask HandleAsync(FileMutated e, CancellationToken ct) => probe.Record(nameof(Second), e);
    }

    private sealed class Third(Probe probe) : IEventHandler<FileMutated>
    {
        public ValueTask HandleAsync(FileMutated e, CancellationToken ct) => probe.Record(nameof(Third), e);
    }

    private sealed class Sibling(Probe probe) : IEventHandler<FileRejected>
    {
        public ValueTask HandleAsync(FileRejected e, CancellationToken ct) => probe.Record(nameof(Sibling), e);
    }

    private sealed class Sentinel(Probe probe) : IEventHandler<BatchAborted>
    {
        public ValueTask HandleAsync(BatchAborted e, CancellationToken ct) => probe.Record(nameof(Sentinel), e);
    }

    private sealed class BaseListener(Probe probe) : IEventHandler<BatchEvent>
    {
        public ValueTask HandleAsync(BatchEvent e, CancellationToken ct) => probe.Record(nameof(BaseListener), e);
    }

    private sealed class Thrower : IEventHandler<FileMutated>
    {
        public ValueTask HandleAsync(FileMutated e, CancellationToken ct) => throw new InvalidOperationException("boom");
    }

    private sealed class Canceller : IEventHandler<FileMutated>
    {
        public ValueTask HandleAsync(FileMutated e, CancellationToken ct) => throw new OperationCanceledException("not the host's token");
    }

    private sealed class Slow(Probe probe) : IEventHandler<FileMutated>
    {
        public async ValueTask HandleAsync(FileMutated e, CancellationToken ct)
        {
            await probe.Record(nameof(Slow), e);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    private sealed class Gated(Probe probe) : IEventHandler<FileMutated>
    {
        public async ValueTask HandleAsync(FileMutated e, CancellationToken ct)
        {
            await probe.Record(nameof(Gated), e);
            var stopped = new TaskCompletionSource();
            await using (ct.Register(() => stopped.TrySetResult()))
            {
                await stopped.Task;
            }
        }
    }

    private sealed class Cancelling(Probe probe) : IEventHandler<FileMutated>
    {
        public async ValueTask HandleAsync(FileMutated e, CancellationToken ct)
        {
            await probe.Record(nameof(Cancelling), e);
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
        }
    }

    private sealed class Everything : IEventHandler<FileMutated>, IEventHandler<FileRejected>,
        IEventHandler<BatchChunkCompleted>, IEventHandler<BatchCompleted>, IEventHandler<BatchAborted>
    {
        private readonly Probe _probe;

        public Everything(Probe probe)
        {
            _probe = probe;
            probe.Constructing();
        }

        public ValueTask HandleAsync(FileMutated e, CancellationToken ct) => _probe.Record(nameof(Everything), e);

        public ValueTask HandleAsync(FileRejected e, CancellationToken ct) => _probe.Record(nameof(Everything), e);

        public ValueTask HandleAsync(BatchChunkCompleted e, CancellationToken ct) => _probe.Record(nameof(Everything), e);

        public ValueTask HandleAsync(BatchCompleted e, CancellationToken ct) => _probe.Record(nameof(Everything), e);

        public ValueTask HandleAsync(BatchAborted e, CancellationToken ct) => _probe.Record(nameof(Everything), e);
    }
}

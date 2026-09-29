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
            // Stopping cancels the token the slow handler waits on.
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

    private sealed class Probe
    {
        private readonly SemaphoreSlim _handled = new(0);

        public ConcurrentQueue<(string Handler, BatchEvent Event)> Calls { get; } = new();

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

    private sealed class Second(Probe probe) : IEventHandler<FileMutated>
    {
        public ValueTask HandleAsync(FileMutated e, CancellationToken ct) => probe.Record(nameof(Second), e);
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
}

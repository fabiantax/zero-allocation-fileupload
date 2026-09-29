using System.Diagnostics.Metrics;
using FileMutation.Domain.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FileMutation.Infrastructure.Events;

/// <summary>
/// Reads the event queue and runs every matching handler, one at a time, in registration order.
/// Each handler call has its own catch: since .NET 6 an exception escaping <see cref="BackgroundService"/>
/// stops the host, so one faulty handler would otherwise take the API down.
/// </summary>
internal sealed partial class EventDispatcherService : BackgroundService
{
    private static readonly Meter Meter = new("FileMutation.Events");
    private static readonly Counter<long> HandlerFailures = Meter.CreateCounter<long>("filemutation.events.handler_failures");

    private readonly IEventStream _stream;
    private readonly IEventSubscription[] _subscriptions;
    private readonly IServiceProvider _services;
    private readonly ILogger<EventDispatcherService> _logger;

    public EventDispatcherService(
        IEventStream stream,
        IEnumerable<IEventSubscription> subscriptions,
        IServiceProvider services,
        ILogger<EventDispatcherService> logger)
    {
        _stream = stream;
        _subscriptions = [.. subscriptions];
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _stream.Reader;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                // Checked before each read so a stop request leaves the rest of the queue untouched
                // instead of feeding it to handlers that ignore the token.
                while (!stoppingToken.IsCancellationRequested && reader.TryRead(out var batchEvent))
                {
                    await DispatchAsync(batchEvent, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping: a clean end, not a failure. Filtered on the token so a
            // cancellation from any other source still surfaces.
        }
    }

    private async Task DispatchAsync(BatchEvent batchEvent, CancellationToken stoppingToken)
    {
        foreach (var subscription in _subscriptions)
        {
            try
            {
                if (subscription.CanHandle(batchEvent))
                {
                    await subscription.HandleAsync(_services, batchEvent, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // Shutdown cancellation is left to propagate to ExecuteAsync; anything else, including a handler
                // that throws OperationCanceledException on its own, is a failure of that handler alone.
                HandlerFailures.Add(
                    1,
                    new KeyValuePair<string, object?>("handler", subscription.HandlerName),
                    new KeyValuePair<string, object?>("event", batchEvent.GetType().Name));
                LogHandlerFailed(exception, subscription.HandlerName, batchEvent.GetType().Name, batchEvent.BatchId.Value, batchEvent.Sequence);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Event handler {Handler} failed on {EventType} (batch {BatchId}, sequence {Sequence}); later handlers and events still run.")]
    private partial void LogHandlerFailed(Exception exception, string handler, string eventType, string batchId, long sequence);
}

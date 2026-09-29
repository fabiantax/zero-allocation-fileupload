using System.Diagnostics.Metrics;
using System.Threading.Channels;
using FileMutation.Domain.Events;
using FileMutation.Domain.Ports;

namespace FileMutation.Infrastructure.Events;

/// <summary>
/// Publishes to one unbounded channel, so a single FIFO keeps per-batch order. Capacity is enforced
/// on a separate count of queued non-terminal events, because a bounded channel cannot exempt terminal events.
/// </summary>
internal sealed class ChannelEventPublisher : IEventPublisher, IEventStream
{
    private static readonly Meter Meter = new("FileMutation.Events");
    private static readonly Counter<long> Dropped = Meter.CreateCounter<long>("filemutation.events.dropped");

    private readonly Channel<BatchEvent> _channel = Channel.CreateUnbounded<BatchEvent>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly int _capacity;
    private int _queuedNonTerminal;

    public ChannelEventPublisher(EventBusOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _capacity = options.Capacity;
        Reader = new CountingReader(_channel.Reader, this);
    }

    public ChannelReader<BatchEvent> Reader { get; }

    public bool TryPublish(BatchEvent batchEvent)
    {
        ArgumentNullException.ThrowIfNull(batchEvent);

        if (batchEvent.IsTerminal)
        {
            _channel.Writer.TryWrite(batchEvent);
            return true;
        }

        // Compare-and-swap rather than increment-then-check: a losing publisher never inflates the
        // count, so the queue holds exactly Capacity events under any number of concurrent publishers.
        int queued;
        do
        {
            queued = Volatile.Read(ref _queuedNonTerminal);
            if (queued >= _capacity)
            {
                Dropped.Add(1);
                return false;
            }
        }
        while (Interlocked.CompareExchange(ref _queuedNonTerminal, queued + 1, queued) != queued);

        _channel.Writer.TryWrite(batchEvent);
        return true;
    }

    private sealed class CountingReader(ChannelReader<BatchEvent> inner, ChannelEventPublisher owner)
        : ChannelReader<BatchEvent>
    {
        public override bool TryRead(out BatchEvent item)
        {
            if (!inner.TryRead(out item!))
            {
                return false;
            }

            if (!item.IsTerminal)
            {
                Interlocked.Decrement(ref owner._queuedNonTerminal);
            }

            return true;
        }

        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
            inner.WaitToReadAsync(cancellationToken);
    }
}

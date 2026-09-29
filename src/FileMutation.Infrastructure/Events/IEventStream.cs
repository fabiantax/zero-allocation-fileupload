using System.Threading.Channels;
using FileMutation.Domain.Events;

namespace FileMutation.Infrastructure.Events;

/// <summary>The consuming side of the event queue.</summary>
/// <remarks>Public so the event dispatcher's tests can read the queue through the registered
/// service, which the repo requires instead of InternalsVisibleTo (same precedent as <c>Program</c>).</remarks>
public interface IEventStream
{
    /// <summary>Gets the reader of the single queue. Reading a non-terminal event frees capacity for the publisher.</summary>
    ChannelReader<BatchEvent> Reader { get; }
}

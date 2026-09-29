namespace FileMutation.Infrastructure.Events;

/// <summary>Capacity of the non-terminal part of the event queue.</summary>
public sealed class EventBusOptions
{
    /// <summary>Gets or sets how many non-terminal events may be queued before further ones are dropped.</summary>
    public int Capacity { get; set; } = 1_024;
}

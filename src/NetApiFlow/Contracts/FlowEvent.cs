using NetApiFlow.Domain;

namespace NetApiFlow;

/// <summary>A single event published by the recorder, as delivered to <see cref="IFlowEventListener"/>.</summary>
public sealed record FlowEvent
{
    /// <summary>Sequential id, per process. Never resets per trace.</summary>
    public required long EventId { get; init; }

    /// <summary>Kind of event.</summary>
    public required FlowEventType Type { get; init; }

    /// <summary>Id of the trace this event belongs to.</summary>
    public required string TraceId { get; init; }

    /// <summary>UTC timestamp when the event was published.</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>Snapshot of the span, present for span events.</summary>
    public FlowSpan? Span { get; init; }

    /// <summary>Snapshot of the trace, present for trace events.</summary>
    public FlowTrace? Trace { get; init; }
}

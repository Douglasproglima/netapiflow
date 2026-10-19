namespace NetApiFlow.Domain;

/// <summary>A single traced step within a <see cref="FlowTrace"/>.</summary>
public sealed record FlowSpan
{
    private static readonly IReadOnlyDictionary<string, string> EmptyTags =
        new Dictionary<string, string>();

    /// <summary>Id of the <see cref="FlowTrace"/> this span belongs to.</summary>
    public required string TraceId { get; init; }

    /// <summary>Id of this span, same as the underlying <c>Activity.SpanId</c>.</summary>
    public required string SpanId { get; init; }

    /// <summary>Id of the parent span, or <see langword="null"/> for the root span of the trace.</summary>
    public string? ParentSpanId { get; init; }

    /// <summary>Order in which this span started, relative to the other spans of the same trace.</summary>
    public required int Sequence { get; init; }

    /// <summary>Name of the component that owns this step (e.g. a class name).</summary>
    public required string Component { get; init; }

    /// <summary>Name of the operation performed (e.g. a method name).</summary>
    public required string Operation { get; init; }

    /// <summary>Kind of component this span represents.</summary>
    public required ComponentType Type { get; init; }

    /// <summary>UTC timestamp when the span started.</summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>UTC timestamp when the span ended, or <see langword="null"/> while still running.</summary>
    public DateTime? EndedAt { get; init; }

    /// <summary>Duration in microseconds, or <see langword="null"/> while still running.</summary>
    public long? DurationMicros { get; init; }

    /// <summary>Current execution status of the span.</summary>
    public required FlowStatus Status { get; init; }

    /// <summary>Type name of the exception that failed this span, when applicable.</summary>
    public string? ErrorType { get; init; }

    /// <summary>Sanitized, truncated error message, when applicable.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Whether the exception originated in this span, as opposed to an ancestor marked as error because a descendant failed.</summary>
    public bool IsErrorOrigin { get; init; }

    /// <summary>Explicit tags attached to the span via <c>FlowStep.Tag(...)</c>.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = EmptyTags;
}

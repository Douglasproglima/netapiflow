namespace NetApiFlow;

/// <summary>Summary of a <see cref="Domain.FlowTrace"/>, without its spans, for list views.</summary>
/// <remarks>Provisional shape. The authoritative REST contract is frozen in Fase 3, tarefa 3.1.</remarks>
public sealed record FlowTraceSummary
{
    /// <summary>Trace id.</summary>
    public required string TraceId { get; init; }

    /// <summary>HTTP method.</summary>
    public required string Method { get; init; }

    /// <summary>Route template.</summary>
    public required string RouteTemplate { get; init; }

    /// <summary>HTTP status code of the response.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>Execution status.</summary>
    public required FlowStatus Status { get; init; }

    /// <summary>UTC timestamp when the trace started.</summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>Total duration, in microseconds.</summary>
    public long? DurationMicros { get; init; }

    /// <summary>Whether spans were discarded because the per-trace span limit was reached.</summary>
    public bool Truncated { get; init; }
}

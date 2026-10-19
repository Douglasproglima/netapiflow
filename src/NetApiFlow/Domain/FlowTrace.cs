namespace NetApiFlow.Domain;

/// <summary>A single traced HTTP request, with its summary and the spans it recorded.</summary>
public sealed record FlowTrace
{
    private static readonly IReadOnlyDictionary<string, string> EmptyTags =
        new Dictionary<string, string>();

    /// <summary>Correlation id, same as the ASP.NET Core <c>Activity.TraceId</c> for the request.</summary>
    public required string TraceId { get; init; }

    /// <summary>HTTP method of the request (e.g. <c>GET</c>).</summary>
    public required string Method { get; init; }

    /// <summary>Route template of the request (e.g. <c>/api/orders/{id}</c>), never the resolved URI.</summary>
    public required string RouteTemplate { get; init; }

    /// <summary>HTTP status code of the response, or <see langword="null"/> while the trace is still running.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>Current execution status of the trace.</summary>
    public required FlowStatus Status { get; init; }

    /// <summary>UTC timestamp when the trace started.</summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>UTC timestamp when the trace ended, or <see langword="null"/> while still running.</summary>
    public DateTime? EndedAt { get; init; }

    /// <summary>Total duration in microseconds, or <see langword="null"/> while still running.</summary>
    public long? DurationMicros { get; init; }

    /// <summary>Number of spans recorded for this trace.</summary>
    public int SpanCount { get; init; }

    /// <summary>Whether spans were discarded because the per-trace span limit was reached.</summary>
    public bool Truncated { get; init; }

    /// <summary>Sanitized, truncated error message, or <see langword="null"/> when the trace has no error.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Explicit tags attached to the trace, e.g. the resolved tenant.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = EmptyTags;
}

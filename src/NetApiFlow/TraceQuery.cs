namespace NetApiFlow;

/// <summary>Filter and pagination for <see cref="ITraceStore.QueryAsync"/>.</summary>
/// <remarks>Provisional shape. The authoritative REST contract is frozen in Fase 3, tarefa 3.1.</remarks>
public sealed record TraceQuery
{
    /// <summary>Filters by trace status.</summary>
    public FlowStatus? Status { get; init; }

    /// <summary>Filters by HTTP method.</summary>
    public string? Method { get; init; }

    /// <summary>Filters by route template.</summary>
    public string? RouteTemplate { get; init; }

    /// <summary>Filters by tenant tag.</summary>
    public string? Tenant { get; init; }

    /// <summary>Only traces started at or after this UTC timestamp.</summary>
    public DateTime? From { get; init; }

    /// <summary>Only traces started at or before this UTC timestamp.</summary>
    public DateTime? To { get; init; }

    /// <summary>Only traces with a duration at or above this value.</summary>
    public long? MinDurationMicros { get; init; }

    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Number of items per page.</summary>
    public int PageSize { get; init; } = 50;
}

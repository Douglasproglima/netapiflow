namespace NetApiFlow;

/// <summary>Filter and pagination for <see cref="ITraceStore.QueryAsync"/>.</summary>
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

    /// <summary>Field to sort by. <see langword="null"/> keeps the default (<see cref="TraceSortField.StartedAt"/> descending).</summary>
    public TraceSortField? SortBy { get; init; }

    /// <summary>Direction <see cref="SortBy"/> sorts in. Ignored when <see cref="SortBy"/> is <see langword="null"/>.</summary>
    public SortDirection? SortDirection { get; init; }
}

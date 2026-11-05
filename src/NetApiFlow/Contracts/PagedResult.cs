namespace NetApiFlow;

/// <summary>A page of results, with the total count across all pages.</summary>
public sealed record PagedResult<T>
{
    /// <summary>Items in this page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Total number of items across all pages.</summary>
    public required int TotalCount { get; init; }
}

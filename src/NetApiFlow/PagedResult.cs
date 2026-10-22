namespace NetApiFlow;

/// <summary>A page of results, with the total count across all pages.</summary>
/// <remarks>Provisional shape. The authoritative REST contract is frozen in Fase 3, tarefa 3.1.</remarks>
public sealed record PagedResult<T>
{
    /// <summary>Items in this page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Total number of items across all pages.</summary>
    public required int TotalCount { get; init; }
}

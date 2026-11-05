namespace NetApiFlow;

/// <summary>Direction a <see cref="TraceQuery"/> sorts <see cref="ITraceStore.QueryAsync"/> results in.</summary>
public enum SortDirection
{
    /// <summary>Lowest value first.</summary>
    Ascending,

    /// <summary>Highest value first.</summary>
    Descending,
}

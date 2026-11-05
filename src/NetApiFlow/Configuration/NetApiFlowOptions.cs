namespace NetApiFlow;

/// <summary>Configuration for NetApiFlow, bound from the <c>NetApiFlow</c> configuration section.</summary>
public sealed class NetApiFlowOptions
{
    private readonly List<string> _includePatterns = [];
    private readonly List<string> _excludePatterns = [];

    /// <summary>
    /// Base path where the dashboard is mounted. Routes under it are never traced, even without
    /// explicit configuration. Set internally by <c>UseNetApiFlowDashboard</c>. Default: <c>/flow</c>.
    /// </summary>
    internal string DashboardBasePath { get; set; } = "/flow";

    /// <summary>Whether tracing is active. Default: <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Duration above which a span is considered slow. Default: 250 ms.</summary>
    public TimeSpan SlowThreshold { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Whether to record the SQL command text (without parameter values) for database spans. Default: <see langword="false"/>.</summary>
    public bool CaptureSqlText { get; set; }

    /// <summary>Ecosystem <c>ActivitySource</c> instances to also observe, besides NetApiFlow's own (ADR-0012). Default: <see cref="ExternalActivitySource.None"/>.</summary>
    public ExternalActivitySource ExternalSources { get; set; } = ExternalActivitySource.None;

    /// <summary>Retention policy for persisted traces.</summary>
    public RetentionOptions Retention { get; } = new();

    /// <summary>In-process limits for tracing overhead.</summary>
    public LimitsOptions Limits { get; } = new();

    /// <summary>Tail-based sampling of persisted traces (D-82).</summary>
    public TailSamplingOptions TailSampling { get; } = new();

    /// <summary>Route glob patterns (<c>*</c> for one segment, <c>**</c> for any depth) a request must match to be traced.</summary>
    public IReadOnlyList<string> IncludePatterns => _includePatterns;

    /// <summary>Route glob patterns excluded from tracing even when they match an include pattern.</summary>
    public IReadOnlyList<string> ExcludePatterns => _excludePatterns;

    /// <summary>Adds route glob patterns that must match for a request to be traced.</summary>
    public NetApiFlowOptions IncludePaths(params string[] patterns)
    {
        _includePatterns.AddRange(patterns);
        return this;
    }

    /// <summary>Adds route glob patterns that are always excluded from tracing, even when they match an include pattern.</summary>
    public NetApiFlowOptions ExcludePaths(params string[] patterns)
    {
        _excludePatterns.AddRange(patterns);
        return this;
    }
}

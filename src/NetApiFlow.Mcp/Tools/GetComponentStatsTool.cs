using System.ComponentModel;
using ModelContextProtocol.Server;

namespace NetApiFlow.Mcp.Tools;

/// <summary>MCP tool getting aggregated component statistics, mirroring <c>GET /flow/api/metrics/components</c>.</summary>
[McpServerToolType]
public static class GetComponentStatsTool
{
    /// <summary>Gets aggregated statistics per component/operation pair.</summary>
    /// <param name="from">Only include spans from traces started at or after this UTC timestamp.</param>
    /// <param name="to">Only include spans from traces started at or before this UTC timestamp.</param>
    /// <param name="store">Trace store to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Statistics ordered by execution count, descending.</returns>
    [McpServerTool(Name = "get_component_stats", ReadOnly = true)]
    [Description("Gets aggregated NetApiFlow statistics per component/operation pair, ordered by execution count.")]
    public static async Task<IReadOnlyList<ComponentStat>> GetComponentStats(
        [Description("Only include spans from traces started at or after this UTC timestamp.")] DateTime? from = null,
        [Description("Only include spans from traces started at or before this UTC timestamp.")] DateTime? to = null,
        ITraceStore store = null!,
        CancellationToken cancellationToken = default)
    {
        var stats = await store.GetComponentStatsAsync(new StatsQuery { From = from, To = to }, cancellationToken);
        return [.. stats.OrderByDescending(s => s.ExecutionCount)];
    }
}

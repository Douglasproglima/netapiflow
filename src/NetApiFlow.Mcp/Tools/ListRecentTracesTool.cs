using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace NetApiFlow.Mcp.Tools;

/// <summary>MCP tool listing recent traces, mirroring <c>GET /flow/api/traces</c>.</summary>
[McpServerToolType]
public static class ListRecentTracesTool
{
    /// <summary>Lists recent traces, most recent first, with optional filters.</summary>
    /// <param name="status">Filters by trace status: <c>Running</c>, <c>Success</c> or <c>Error</c>.</param>
    /// <param name="method">Filters by HTTP method, e.g. <c>GET</c>.</param>
    /// <param name="route">Filters by route template, e.g. <c>/api/orders/{id}</c>.</param>
    /// <param name="limit">Maximum number of traces to return. Between 1 and 200, default 50.</param>
    /// <param name="store">Trace store to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching traces.</returns>
    /// <exception cref="McpException"><paramref name="status"/> or <paramref name="limit"/> is invalid.</exception>
    [McpServerTool(Name = "list_recent_traces", ReadOnly = true)]
    [Description("Lists recent NetApiFlow traces, most recent first, with optional filters.")]
    public static async Task<IReadOnlyList<FlowTraceSummary>> ListRecentTraces(
        [Description("Filters by trace status: Running, Success or Error.")] string? status = null,
        [Description("Filters by HTTP method, e.g. GET.")] string? method = null,
        [Description("Filters by route template, e.g. /api/orders/{id}.")] string? route = null,
        [Description("Maximum number of traces to return. Between 1 and 200, default 50.")] int? limit = null,
        ITraceStore store = null!,
        CancellationToken cancellationToken = default)
    {
        FlowStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<FlowStatus>(status, ignoreCase: true, out var resolvedStatus))
            {
                throw new McpException($"'{status}' is not a valid trace status. Accepted values: {string.Join(", ", Enum.GetNames<FlowStatus>())}.");
            }

            parsedStatus = resolvedStatus;
        }

        if (limit is < 1 or > 200)
        {
            throw new McpException($"'{limit}' is out of range. limit must be between 1 and 200.");
        }

        var query = new TraceQuery
        {
            Status = parsedStatus,
            Method = method,
            RouteTemplate = route,
            PageSize = limit ?? 50,
        };

        var result = await store.QueryAsync(query, cancellationToken);
        return result.Items;
    }
}

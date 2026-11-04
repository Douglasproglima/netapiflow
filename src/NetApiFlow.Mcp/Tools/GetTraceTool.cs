using System.ComponentModel;
using ModelContextProtocol.Server;

namespace NetApiFlow.Mcp.Tools;

/// <summary>MCP tool getting a single trace by id, mirroring <c>GET /flow/api/traces/{traceId}</c>.</summary>
[McpServerToolType]
public static class GetTraceTool
{
    /// <summary>Gets a single trace with its spans by id.</summary>
    /// <param name="traceId">The trace id.</param>
    /// <param name="store">Trace store to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="TraceLookupResult"/> describing whether the trace was found.</returns>
    [McpServerTool(Name = "get_trace", ReadOnly = true)]
    [Description("Gets a single NetApiFlow trace with its spans by id.")]
    public static async Task<TraceLookupResult> GetTrace(
        [Description("The trace id.")] string traceId,
        ITraceStore store = null!,
        CancellationToken cancellationToken = default)
    {
        var detail = await store.GetAsync(traceId, cancellationToken);

        return detail is null
            ? new TraceLookupResult { Found = false, Message = $"No trace found with id '{traceId}'." }
            : new TraceLookupResult { Found = true, Trace = detail };
    }
}

/// <summary>Result of <see cref="GetTraceTool.GetTrace"/>.</summary>
public sealed record TraceLookupResult
{
    /// <summary>Whether a matching trace was found.</summary>
    public required bool Found { get; init; }

    /// <summary>Explanation for <see cref="Found"/> being <see langword="false"/>.</summary>
    public string? Message { get; init; }

    /// <summary>The trace, when <see cref="Found"/> is <see langword="true"/>.</summary>
    public FlowTraceDetail? Trace { get; init; }
}

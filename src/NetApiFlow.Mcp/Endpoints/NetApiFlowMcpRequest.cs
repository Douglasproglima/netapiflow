using Microsoft.AspNetCore.Http;

namespace NetApiFlow.Mcp.Endpoints;

internal static class NetApiFlowMcpRequest
{
    private static readonly AsyncLocal<bool> CameThroughTheNetApiFlowEndpoint = new();

    public static bool IsCurrent => new HttpContextAccessor().HttpContext?.Request.Path.StartsWithSegments("/flow/mcp") == true;

    public static void MarkCurrent() => CameThroughTheNetApiFlowEndpoint.Value = true;
}

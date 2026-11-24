namespace NetApiFlow.Mcp.Endpoints;

internal static class NetApiFlowMcpRequest
{
    private static readonly AsyncLocal<bool> CameThroughTheNetApiFlowEndpoint = new();

    public static bool IsCurrent => CameThroughTheNetApiFlowEndpoint.Value;

    public static void MarkCurrent() => CameThroughTheNetApiFlowEndpoint.Value = true;
}

namespace NetApiFlow.Mcp.Endpoints;

internal static class NetApiFlowMcpRequest
{
    private static bool cameThroughTheNetApiFlowEndpoint;

    public static bool IsCurrent => cameThroughTheNetApiFlowEndpoint;

    public static void MarkCurrent() => cameThroughTheNetApiFlowEndpoint = true;
}

namespace NetApiFlow.Middleware;

internal static class FlowHttpContextItems
{
    public const string TraceStarted = "NetApiFlow.TraceStarted";

    public const string ImplicitEndpointStep = "NetApiFlow.ImplicitEndpointStep";

    public const string SwallowedException = "NetApiFlow.SwallowedException";

    public const string RequestState = "NetApiFlow.RequestState";
}

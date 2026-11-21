using System.Diagnostics;
using NetApiFlow.Middleware;

namespace NetApiFlow.Instrumentation;

internal static class FlowTraceResolver
{
    private const string RequestStateProperty = "netapiflow.request";

    private static readonly AsyncLocal<RequestHolder?> AmbientRequest = new();

    public static void Attach(Activity? serverActivity, FlowRequestState state)
    {
        serverActivity?.SetCustomProperty(RequestStateProperty, state);
        AmbientRequest.Value = new RequestHolder(state, serverActivity);
    }

    public static void Detach()
    {
        if (AmbientRequest.Value is { } holder)
        {
            holder.ServerActivity?.SetCustomProperty(RequestStateProperty, null);
            holder.State = null;
        }

        AmbientRequest.Value = null;
    }

    public static FlowRequestState? ForActivity(Activity activity) =>
        FromAmbientRequest(activity.TraceId) ?? FromParents(activity.Parent);

    public static FlowRequestState? ForSampling(ActivityTraceId candidateTraceId) =>
        FromAmbientRequest(candidateTraceId) ?? FromParents(Activity.Current is { } current && current.TraceId == candidateTraceId ? current : null);

    public static FlowRequestState? ForCurrentActivity()
    {
        var current = Activity.Current;
        var ambient = AmbientRequest.Value?.State;
        if (ambient is not null && (current is null ? !ambient.HasW3CTraceId : ambient.W3CTraceId == current.TraceId))
        {
            return ambient;
        }

        return FromParents(current);
    }

    private static FlowRequestState? FromAmbientRequest(ActivityTraceId traceId)
    {
        var ambient = AmbientRequest.Value?.State;
        return ambient is not null && ambient.HasW3CTraceId && ambient.W3CTraceId == traceId ? ambient : null;
    }

    private static FlowRequestState? FromParents(Activity? start)
    {
        for (var activity = start; activity is not null; activity = activity.Parent)
        {
            if (activity.GetCustomProperty(RequestStateProperty) is FlowRequestState state)
            {
                return state;
            }
        }

        return null;
    }

    private sealed class RequestHolder(FlowRequestState state, Activity? serverActivity)
    {
        public FlowRequestState? State { get; set; } = state;

        public Activity? ServerActivity { get; } = serverActivity;
    }
}

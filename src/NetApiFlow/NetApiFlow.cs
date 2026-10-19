using System.Diagnostics;
using NetApiFlow.Instrumentation;

namespace NetApiFlow;

/// <summary>Entry point for manually marking a tracked step.</summary>
public static class NetApiFlow
{
    /// <summary>Marks the start of a tracked step. Dispose the returned <see cref="FlowStep"/> to close it.</summary>
    public static FlowStep Step(string component, string operation, ComponentType type = ComponentType.Custom)
    {
        var activity = FlowActivitySource.Source.StartActivity($"{component}.{operation}", ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(FlowTagNames.Component, component);
            activity.SetTag(FlowTagNames.Operation, operation);
            activity.SetTag(FlowTagNames.ComponentType, type.ToString());
        }

        return new FlowStep(activity);
    }

    /// <summary>Trace id of the request currently executing, or <see langword="null"/> when tracing is disabled or there is no ambient trace.</summary>
    public static string? CurrentTraceId => Activity.Current?.TraceId.ToString();
}

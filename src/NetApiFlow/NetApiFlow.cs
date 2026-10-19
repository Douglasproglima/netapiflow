using System.Diagnostics;
using NetApiFlow.Instrumentation;

namespace NetApiFlow;

/// <summary>Entry point for manually marking a tracked step.</summary>
public static class NetApiFlow
{
    /// <summary>Marks the start of a tracked step. Dispose the returned <see cref="FlowStep"/> to close it.</summary>
    public static FlowStep Step(string component, string operation, ComponentType type = ComponentType.Custom)
    {
        if (!FlowActivitySource.Source.HasListeners())
        {
            return new FlowStep(null);
        }

        var tags = new ActivityTagsCollection
        {
            [FlowTagNames.Component] = component,
            [FlowTagNames.Operation] = operation,
            [FlowTagNames.ComponentType] = type.ToString(),
        };

        var activity = FlowActivitySource.Source.StartActivity(
            kind: ActivityKind.Internal,
            tags: tags,
            name: $"{component}.{operation}");

        return new FlowStep(activity);
    }

    /// <summary>Trace id of the request currently executing, or <see langword="null"/> when tracing is disabled or there is no ambient trace.</summary>
    public static string? CurrentTraceId => Activity.Current?.TraceId.ToString();
}

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;

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

    /// <summary>A trace that finished recently, read straight from memory, without waiting for the persistence
    /// queue. Meant for an integration test that just awaited its own HTTP call and needs the result immediately:
    /// read <c>X-NetApiFlow-TraceId</c> off the response, then call this. Only looks at the in-memory buffer of
    /// recently-completed traces, never falls back to <see cref="ITraceStore"/>, so it never races the
    /// persistence queue. <see langword="null"/> when the trace already aged out of the buffer or tracing was
    /// never registered.</summary>
    public static FlowTraceDetail? GetRecentTrace(IServiceProvider services, string traceId) =>
        services.GetService<FlowRecorder>()?.GetRecentlyCompleted(traceId);
}

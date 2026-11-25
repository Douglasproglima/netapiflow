using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;

namespace NetApiFlow;

/// <summary>Entry point for manually marking a tracked step: <c>Flow.Step("OrderRepository", "Find")</c>.</summary>
// kept for ADR-0099
public static class Flow
{
    /// <summary>Marks the start of a tracked step. Dispose the returned <see cref="FlowStep"/> to close it.
    /// A null <paramref name="component"/> or <paramref name="operation"/> returns a step that records nothing, and a warning is logged.</summary>
    /// <param name="component">The component the step belongs to, such as a class name.</param>
    /// <param name="operation">The operation the step runs, such as a method name.</param>
    /// <param name="type">The kind of component, shown in the graph.</param>
    /// <returns>The open step.</returns>
    public static FlowStep Step(string component, string operation, ComponentType type = ComponentType.Custom)
    {
        if (component is null || operation is null)
        {
            NullStepArgumentLog.StepIgnored();
            return new FlowStep(null);
        }

        if (FlowActivitySource.EveryRegisteredListenerIsDisabled || !FlowActivitySource.Source.HasListeners())
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

    /// <summary>NetApiFlow id of the trace of the request currently executing, or <see langword="null"/> when that request has no trace in progress (tracing is disabled, the request is not traced, or there is no ambient request).
    /// It is not the W3C trace id: read that one from <c>Activity.Current.TraceId</c>.</summary>
    public static string? CurrentTraceId => FlowTraceResolver.ForCurrentActivity() is { IsRecording: true } state ? state.TraceId : null;

    /// <summary>A trace that finished recently, read straight from memory, without waiting for the persistence
    /// queue. Meant for an integration test that just awaited its own HTTP call and needs the result immediately:
    /// read <c>X-NetApiFlow-TraceId</c> off the response, then call this. Only looks at the in-memory buffer of
    /// recently-completed traces, never falls back to <see cref="ITraceStore"/>, so it never races the
    /// persistence queue. <see langword="null"/> when the trace already aged out of the buffer or tracing was
    /// never registered.
    /// <para>Written for an integration test, and only for one: it takes an <see cref="IServiceProvider"/>
    /// because a test host has one at hand and no injected dependencies. Do not call it from inside a
    /// request pipeline, a controller or a service. There it is a service locator, it reads a buffer that
    /// is deliberately small and lossy, and the trace it would return is the one still being recorded.</para></summary>
    /// <param name="services">The service provider of the host under test.</param>
    /// <param name="traceId">The NetApiFlow id of the trace, from the <c>X-NetApiFlow-TraceId</c> header.</param>
    /// <returns>The trace, or <see langword="null"/>.</returns>
    public static FlowTraceDetail? GetRecentTrace(IServiceProvider services, string traceId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(traceId);
        return services.GetService<FlowRecorder>()?.GetRecentlyCompleted(traceId);
    }
}

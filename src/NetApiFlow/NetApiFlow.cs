using System.ComponentModel;

namespace NetApiFlow;

/// <summary>The former name of <see cref="Flow"/>. Every member forwards to it.</summary>
[Obsolete("Use NetApiFlow.Flow.")]
[EditorBrowsable(EditorBrowsableState.Never)]
public static class NetApiFlow
{
    /// <summary>Forwards to <see cref="Flow.Step"/>.</summary>
    /// <param name="component">The component the step belongs to.</param>
    /// <param name="operation">The operation the step runs.</param>
    /// <param name="type">The kind of component.</param>
    /// <returns>The open step.</returns>
    public static FlowStep Step(string component, string operation, ComponentType type = ComponentType.Custom) =>
        Flow.Step(component, operation, type);

    /// <summary>Forwards to <see cref="Flow.CurrentTraceId"/>.</summary>
    public static string? CurrentTraceId => Flow.CurrentTraceId;

    /// <summary>Forwards to <see cref="Flow.GetRecentTrace"/>.</summary>
    /// <param name="services">The service provider of the host under test.</param>
    /// <param name="traceId">The NetApiFlow id of the trace.</param>
    /// <returns>The trace, or <see langword="null"/>.</returns>
    public static FlowTraceDetail? GetRecentTrace(IServiceProvider services, string traceId) =>
        Flow.GetRecentTrace(services, traceId);
}

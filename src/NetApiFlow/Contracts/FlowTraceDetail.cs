using NetApiFlow.Domain;

namespace NetApiFlow;

/// <summary>A consolidated trace together with the spans it recorded, as returned by <see cref="ITraceStore"/> reads (D-19).</summary>
public sealed record FlowTraceDetail
{
    /// <summary>The trace itself.</summary>
    public required FlowTrace Trace { get; init; }

    /// <summary>Spans recorded for <see cref="Trace"/>, in <c>Sequence</c> order.</summary>
    public required IReadOnlyList<FlowSpan> Spans { get; init; }
}

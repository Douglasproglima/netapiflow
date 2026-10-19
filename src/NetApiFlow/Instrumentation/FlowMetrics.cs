using System.Diagnostics.Metrics;

namespace NetApiFlow.Instrumentation;

/// <summary>The <see cref="Meter"/> named <c>"NetApiFlow"</c> and its counters (seção 6.7 do prompt).</summary>
internal static class FlowMetrics
{
    public const string MeterName = "NetApiFlow";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Traces not tracked because the in-memory active-trace limit was reached.</summary>
    public static readonly Counter<long> DroppedTraces =
        Meter.CreateCounter<long>("netapiflow.traces.dropped", description: "Traces not tracked because the active-trace limit was reached.");

    /// <summary>Spans discarded because the per-trace span limit was reached.</summary>
    public static readonly Counter<long> DroppedSpans =
        Meter.CreateCounter<long>("netapiflow.spans.dropped", description: "Spans discarded because the per-trace span limit was reached.");

    /// <summary>Traces that failed to persist, because the persistence queue was full or the store threw.</summary>
    public static readonly Counter<long> PersistenceFailures =
        Meter.CreateCounter<long>("netapiflow.persistence.failures", description: "Traces that failed to persist.");
}

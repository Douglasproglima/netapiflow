using System.Diagnostics.Metrics;

namespace NetApiFlow.Instrumentation;

internal static class FlowMetrics
{
    public const string MeterName = "NetApiFlow";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> DroppedTraces =
        Meter.CreateCounter<long>("netapiflow.traces.dropped", description: "Traces not tracked because the active-trace limit was reached.");

    public static readonly Counter<long> DroppedSpans =
        Meter.CreateCounter<long>("netapiflow.spans.dropped", description: "Spans discarded because the per-trace span limit was reached.");

    public static readonly Counter<long> PersistenceFailures =
        Meter.CreateCounter<long>("netapiflow.persistence.failures", description: "Traces that failed to persist.");

    public static readonly Counter<long> SampledOutTraces =
        Meter.CreateCounter<long>("netapiflow.traces.sampled_out", description: "Traces discarded by tail-based sampling before reaching the persistence queue.");

    public static readonly Counter<long> DroppedLogs =
        Meter.CreateCounter<long>("netapiflow.logs.dropped", description: "Log entries discarded because the per-span log limit was reached.");

    public static readonly Counter<long> DroppedTags =
        Meter.CreateCounter<long>("netapiflow.tags.dropped", description: "Tags discarded because the per-span tag limit was reached.");

    public static readonly Counter<long> DroppedEvents =
        Meter.CreateCounter<long>("netapiflow.events.dropped", description: "Events not delivered to the listeners because the event queue was full.");
}

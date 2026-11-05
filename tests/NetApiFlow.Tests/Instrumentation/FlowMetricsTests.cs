using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using NetApiFlow.Storage;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

public class FlowMetricsTests
{
    private static MeterListener CreateListener(ConcurrentDictionary<string, long> totals)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == FlowMetrics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
            totals.AddOrUpdate(instrument.Name, measurement, (_, existing) => existing + measurement));

        listener.Start();
        return listener;
    }

    [Fact]
    public void DroppedTraces_IncrementsWhenActiveTraceLimitIsReached()
    {
        var totals = new ConcurrentDictionary<string, long>();
        using var listener = CreateListener(totals);

        var recorder = new FlowRecorder(Options.Create(new NetApiFlowOptions()));
        for (var i = 0; i < FlowRecorder.MaxActiveTraces; i++)
        {
            recorder.TryStartTrace($"trace-{i}", "GET", "/api/orders", DateTime.UtcNow);
        }

        recorder.TryStartTrace("overflow", "GET", "/api/orders", DateTime.UtcNow);

        totals.GetValueOrDefault("netapiflow.traces.dropped").ShouldBe(1);
    }

    [Fact]
    public void DroppedSpans_IncrementsWhenSpanLimitIsReached()
    {
        var totals = new ConcurrentDictionary<string, long>();
        using var listener = CreateListener(totals);

        var options = new NetApiFlowOptions();
        options.Limits.MaxSpansPerTrace = 1;
        var recorder = new FlowRecorder(Options.Create(options));
        recorder.TryStartTrace("trace-1", "GET", "/api/orders", DateTime.UtcNow);

        var span = new FlowSpan
        {
            TraceId = "trace-1",
            SpanId = "span-1",
            Sequence = 0,
            Component = "Component",
            Operation = "Operation",
            Type = ComponentType.Custom,
            StartedAt = DateTime.UtcNow,
            Status = FlowStatus.Running,
        };
        recorder.TryAddSpan("trace-1", span);
        recorder.TryAddSpan("trace-1", span with { SpanId = "span-2" });

        totals.GetValueOrDefault("netapiflow.spans.dropped").ShouldBe(1);
    }

    [Fact]
    public void PersistenceFailures_IncrementsWhenQueueIsFull()
    {
        var totals = new ConcurrentDictionary<string, long>();
        using var listener = CreateListener(totals);

        var store = new NoOpTraceStore();
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        using var writer = new PersistenceWriter(recorder, store, options, NullLogger<PersistenceWriter>.Instance);

        for (var i = 0; i < 1001; i++)
        {
            recorder.TryStartTrace($"trace-{i}", "GET", "/api/orders", DateTime.UtcNow);
            recorder.CompleteTrace($"trace-{i}", 200, FlowStatus.Success, null);
        }

        totals.GetValueOrDefault("netapiflow.persistence.failures").ShouldBe(1);
    }

    [Fact]
    public void SampledOutTraces_IncrementsWhenTailSamplingDiscardsATrace()
    {
        var totals = new ConcurrentDictionary<string, long>();
        using var listener = CreateListener(totals);

        var store = new NoOpTraceStore();
        var options = Options.Create(new NetApiFlowOptions
        {
            TailSampling = { Enabled = true, SuccessSampleRate = 0.0 },
        });
        var recorder = new FlowRecorder(options);
        using var writer = new PersistenceWriter(recorder, store, options, NullLogger<PersistenceWriter>.Instance);

        recorder.TryStartTrace("fast-success", "GET", "/api/orders", DateTime.UtcNow);
        recorder.CompleteTrace("fast-success", 200, FlowStatus.Success, null);

        totals.GetValueOrDefault("netapiflow.traces.sampled_out").ShouldBe(1);
    }

    private sealed class NoOpTraceStore : ITraceStore
    {
        public ValueTask SaveAsync(FlowTrace trace, IReadOnlyList<FlowSpan> spans, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<FlowTraceDetail?> GetAsync(string traceId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<FlowTraceDetail?>(null);

        public ValueTask<PagedResult<FlowTraceSummary>> QueryAsync(TraceQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PagedResult<FlowTraceSummary> { Items = [], TotalCount = 0 });

        public ValueTask<IReadOnlyList<ComponentStat>> GetComponentStatsAsync(StatsQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ComponentStat>>([]);

        public ValueTask<TraceStats> GetTraceStatsAsync(StatsQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new TraceStats { TotalTraces = 0, ErrorCount = 0, AverageDurationMicros = 0, P95DurationMicros = 0, MaxDurationMicros = 0 });

        public ValueTask<IReadOnlyList<RouteStat>> GetRouteStatsAsync(StatsQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RouteStat>>([]);

        public ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeTemplate, CancellationToken cancellationToken) =>
            ValueTask.FromResult<FlowTraceDetail?>(null);

        public ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken) =>
            ValueTask.FromResult(0);
    }
}

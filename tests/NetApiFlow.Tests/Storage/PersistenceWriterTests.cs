using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using NetApiFlow.Storage;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Storage;

public class PersistenceWriterTests
{
    private sealed class FakeTraceStore : ITraceStore
    {
        public List<FlowTrace> Saved { get; } = [];

        public ValueTask SaveAsync(FlowTrace trace, IReadOnlyList<FlowSpan> spans, CancellationToken cancellationToken)
        {
            Saved.Add(trace);
            return ValueTask.CompletedTask;
        }

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

        public ValueTask<ArchitectureMap> GetArchitectureMapAsync(string method, string routeTemplate, StatsQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ArchitectureMap { TotalExecutions = 0, Nodes = [], Edges = [] });

        public ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeTemplate, CancellationToken cancellationToken) =>
            ValueTask.FromResult<FlowTraceDetail?>(null);

        public ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken) =>
            ValueTask.FromResult(0);
    }

    [Fact]
    public void Enqueue_WhenQueueIsFull_IncrementsDroppedCounter()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        using var subject = new PersistenceWriter(recorder, new FakeTraceStore(), options, NullLogger<PersistenceWriter>.Instance);

        for (var i = 0; i < 1000; i++)
        {
            recorder.TryStartTrace($"trace-{i}", "GET", "/api/orders", DateTime.UtcNow);
            recorder.CompleteTrace($"trace-{i}", 200, FlowStatus.Success, null);
        }

        subject.DroppedCount.ShouldBe(0);

        recorder.TryStartTrace("overflow", "GET", "/api/orders", DateTime.UtcNow);
        recorder.CompleteTrace("overflow", 200, FlowStatus.Success, null);

        subject.DroppedCount.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_SavesEnqueuedTracesToStore()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        var store = new FakeTraceStore();
        using var subject = new PersistenceWriter(recorder, store, options, NullLogger<PersistenceWriter>.Instance);

        await subject.StartAsync(CancellationToken.None);
        try
        {
            recorder.TryStartTrace("trace-1", "GET", "/api/orders", DateTime.UtcNow);
            recorder.CompleteTrace("trace-1", 200, FlowStatus.Success, null);

            await WaitUntilAsync(() => store.Saved.Count == 1, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await subject.StopAsync(CancellationToken.None);
        }

        store.Saved.Single().TraceId.ShouldBe("trace-1");
    }

    [Fact]
    public void Enqueue_WhenTailSamplingDiscardsTrace_NeverWritesToChannel()
    {
        var options = Options.Create(new NetApiFlowOptions
        {
            TailSampling = { Enabled = true, SuccessSampleRate = 0.0 },
        });
        var recorder = new FlowRecorder(options);
        var store = new FakeTraceStore();
        using var subject = new PersistenceWriter(recorder, store, options, NullLogger<PersistenceWriter>.Instance);

        recorder.TryStartTrace("fast-success", "GET", "/api/orders", DateTime.UtcNow);
        recorder.CompleteTrace("fast-success", 200, FlowStatus.Success, null);

        subject.DroppedCount.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTailSamplingEnabled_StillPersistsErrorsAndSlowTraces()
    {
        var options = Options.Create(new NetApiFlowOptions
        {
            SlowThreshold = TimeSpan.FromMilliseconds(1),
            TailSampling = { Enabled = true, SuccessSampleRate = 0.0 },
        });
        var recorder = new FlowRecorder(options);
        var store = new FakeTraceStore();
        using var subject = new PersistenceWriter(recorder, store, options, NullLogger<PersistenceWriter>.Instance);

        await subject.StartAsync(CancellationToken.None);
        try
        {
            recorder.TryStartTrace("error-trace", "GET", "/api/orders", DateTime.UtcNow);
            recorder.CompleteTrace("error-trace", 500, FlowStatus.Error, "boom");

            recorder.TryStartTrace("slow-trace", "GET", "/api/orders", DateTime.UtcNow.AddMilliseconds(-10));
            recorder.CompleteTrace("slow-trace", 200, FlowStatus.Success, null);

            recorder.TryStartTrace("fast-success", "GET", "/api/orders", DateTime.UtcNow);
            recorder.CompleteTrace("fast-success", 200, FlowStatus.Success, null);

            await WaitUntilAsync(() => store.Saved.Count == 2, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await subject.StopAsync(CancellationToken.None);
        }

        store.Saved.Select(trace => trace.TraceId).ShouldBe(["error-trace", "slow-trace"], ignoreOrder: true);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue();
    }
}

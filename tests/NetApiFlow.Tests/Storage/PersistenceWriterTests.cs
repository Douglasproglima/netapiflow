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

        public ValueTask<FlowTrace?> GetAsync(string traceId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<FlowTrace?>(null);

        public ValueTask<PagedResult<FlowTraceSummary>> QueryAsync(TraceQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PagedResult<FlowTraceSummary> { Items = [], TotalCount = 0 });

        public ValueTask<IReadOnlyList<ComponentStat>> GetComponentStatsAsync(StatsQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ComponentStat>>([]);

        public ValueTask<FlowTrace?> GetLastSuccessfulAsync(string method, string routeTemplate, CancellationToken cancellationToken) =>
            ValueTask.FromResult<FlowTrace?>(null);

        public ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken) =>
            ValueTask.FromResult(0);
    }

    [Fact]
    public void Enqueue_WhenQueueIsFull_IncrementsDroppedCounter()
    {
        var recorder = new FlowRecorder(Options.Create(new NetApiFlowOptions()));
        using var subject = new PersistenceWriter(recorder, new FakeTraceStore(), NullLogger<PersistenceWriter>.Instance);

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
        var recorder = new FlowRecorder(Options.Create(new NetApiFlowOptions()));
        var store = new FakeTraceStore();
        using var subject = new PersistenceWriter(recorder, store, NullLogger<PersistenceWriter>.Instance);

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

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Events;
using NetApiFlow.Recording;
using NetApiFlow.Storage;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Events;

public class FlowEventPublisherTests
{
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

        public ValueTask<ArchitectureMap> GetArchitectureMapAsync(string method, string routeTemplate, StatsQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ArchitectureMap { TotalExecutions = 0, Nodes = [], Edges = [] });

        public ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeTemplate, CancellationToken cancellationToken) =>
            ValueTask.FromResult<FlowTraceDetail?>(null);

        public ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken) =>
            ValueTask.FromResult(0);
    }

    private sealed class ThrowingListener : IFlowEventListener
    {
        public ValueTask OnEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("listener boom");
    }

    private sealed class RecordingListener : IFlowEventListener
    {
        public TaskCompletionSource<FlowEvent> Received { get; } = new();

        public ValueTask OnEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken)
        {
            Received.TrySetResult(flowEvent);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Publish_WhenOneListenerThrows_OtherListenerStillReceivesEvent()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        using var persistenceWriter = new PersistenceWriter(recorder, new NoOpTraceStore(), options, NullLogger<PersistenceWriter>.Instance);
        var recording = new RecordingListener();
        var listeners = new IFlowEventListener[] { new ThrowingListener(), recording };
        using var publisher = new FlowEventPublisher(recorder, persistenceWriter, listeners, NullLogger<FlowEventPublisher>.Instance);

        var started = recorder.TryStartTrace("trace-1", "GET", "/api/orders", DateTime.UtcNow);
        started.ShouldBeTrue();

        var completedTask = await Task.WhenAny(recording.Received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completedTask.ShouldBe(recording.Received.Task);

        var flowEvent = await recording.Received.Task;
        flowEvent.Type.ShouldBe(FlowEventType.TraceStarted);
        flowEvent.TraceId.ShouldBe("trace-1");
    }
}

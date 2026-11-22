using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Events;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using NetApiFlow.Storage;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Events;

[Collection("FlowMetrics")]
public class FlowEventPublisherTests
{
    private const int EventsPerTraceWithoutSteps = 2;

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

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
        private readonly ConcurrentQueue<FlowEvent> _received = new();

        public IReadOnlyList<FlowEvent> Received => [.. _received];

        public ValueTask OnEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken)
        {
            _received.Enqueue(flowEvent);
            return ValueTask.CompletedTask;
        }

        public async Task<bool> WaitForAsync(Func<IReadOnlyList<FlowEvent>, bool> condition)
        {
            var deadline = DateTime.UtcNow + WaitLimit;
            while (DateTime.UtcNow < deadline)
            {
                if (condition(Received))
                {
                    return true;
                }

                await Task.Delay(10);
            }

            return condition(Received);
        }
    }

    private sealed class GatedListener : IFlowEventListener
    {
        public ManualResetEventSlim Entered { get; } = new(false);

        public ManualResetEventSlim Gate { get; } = new(false);

        public ValueTask OnEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken)
        {
            Entered.Set();
            Gate.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WaitingForShutdownListener : IFlowEventListener
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask OnEventAsync(FlowEvent flowEvent, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private static (FlowRecorder Recorder, PersistenceWriter Writer, FlowEventPublisher Publisher) CreatePublisher(params IFlowEventListener[] listeners)
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        var writer = new PersistenceWriter(recorder, new NoOpTraceStore(), options, NullLogger<PersistenceWriter>.Instance);
        var publisher = new FlowEventPublisher(recorder, writer, listeners, NullLogger<FlowEventPublisher>.Instance);
        return (recorder, writer, publisher);
    }

    private static FlowSpan RunningSpan(string traceId, int sequence) => new()
    {
        TraceId = traceId,
        SpanId = $"span-{sequence}",
        ParentSpanId = null,
        Sequence = sequence,
        Component = "OrderService",
        Operation = "PlaceAsync",
        Type = ComponentType.Service,
        StartedAt = DateTime.UtcNow,
        Status = FlowStatus.Running,
    };

    private static int TracesFillingTheQueueAndThen(int overflowEvents) =>
        (FlowEventPublisher.QueueCapacity + overflowEvents) / EventsPerTraceWithoutSteps;

    [Fact]
    public async Task Publish_WhenOneListenerThrows_OtherListenerStillReceivesEvent()
    {
        var recording = new RecordingListener();
        var (recorder, writer, publisher) = CreatePublisher(new ThrowingListener(), recording);
        using (writer)
        using (publisher)
        {
            await publisher.StartAsync(CancellationToken.None);

            recorder.TryStartTrace("trace-1", "GET", "/api/orders", DateTime.UtcNow).ShouldBeTrue();

            (await recording.WaitForAsync(received => received.Count > 0)).ShouldBeTrue();
            var flowEvent = recording.Received[0];
            flowEvent.Type.ShouldBe(FlowEventType.TraceStarted);
            flowEvent.TraceId.ShouldBe("trace-1");
            await publisher.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Publish_EventsOfOneTrace_ReachTheListenerInTheOrderTheyWerePublished()
    {
        var recording = new RecordingListener();
        var (recorder, writer, publisher) = CreatePublisher(recording);
        using (writer)
        using (publisher)
        {
            await publisher.StartAsync(CancellationToken.None);
            recorder.TryStartTrace("ordered", "GET", "/api/orders", DateTime.UtcNow).ShouldBeTrue();
            var spans = Enumerable.Range(0, 50).Select(sequence => RunningSpan("ordered", sequence)).ToList();
            foreach (var span in spans)
            {
                recorder.TryAddSpan("ordered", span).ShouldBeTrue();
                recorder.CompleteSpan("ordered", span with { Status = FlowStatus.Success, EndedAt = DateTime.UtcNow, DurationMicros = 1 });
            }

            recorder.CompleteTrace("ordered", 200, FlowStatus.Success, null);

            (await recording.WaitForAsync(received => received.Any(e => e.Type == FlowEventType.TraceCompleted))).ShouldBeTrue();
            var expected = new List<(FlowEventType, string?)> { (FlowEventType.TraceStarted, null) };
            foreach (var span in spans)
            {
                expected.Add((FlowEventType.SpanStarted, span.SpanId));
                expected.Add((FlowEventType.SpanCompleted, span.SpanId));
            }

            expected.Add((FlowEventType.TraceCompleted, null));
            recording.Received.Select(e => (e.Type, e.Span?.SpanId)).ShouldBe(expected);
            recording.Received.Select(e => e.EventId).ShouldBeInOrder(Shouldly.SortDirection.Ascending);
            await publisher.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Publish_WithTheQueueFull_DropsTheEventCountsItAndNeverWaits()
    {
        const int Overflow = 8;
        var dropped = 0L;
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == FlowMetrics.MeterName && instrument.Name == "netapiflow.events.dropped")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<long>((_, measurement, _, _) => Interlocked.Add(ref dropped, measurement));
        meterListener.Start();

        var gated = new GatedListener();
        var (recorder, writer, publisher) = CreatePublisher(gated);
        using (writer)
        using (publisher)
        {
            try
            {
                await publisher.StartAsync(CancellationToken.None);
                recorder.TryStartTrace("held", "GET", "/api/orders", DateTime.UtcNow).ShouldBeTrue();
                gated.Entered.Wait(WaitLimit).ShouldBeTrue("the consumer never took the first event");

                var stopwatch = Stopwatch.StartNew();
                for (var trace = 0; trace < TracesFillingTheQueueAndThen(Overflow); trace++)
                {
                    recorder.TryStartTrace($"flood-{trace}", "GET", "/api/orders", DateTime.UtcNow).ShouldBeTrue();
                    recorder.CompleteTrace($"flood-{trace}", 200, FlowStatus.Success, null);
                }

                stopwatch.Stop();

                Interlocked.Read(ref dropped).ShouldBe(Overflow);
                stopwatch.Elapsed.ShouldBeLessThan(WaitLimit);
            }
            finally
            {
                gated.Gate.Set();
            }

            await publisher.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StopAsync_WithAListenerStillHandlingAnEvent_CancelsItAndFinishesQuicklyWithoutThrowing()
    {
        var waiting = new WaitingForShutdownListener();
        var (recorder, writer, publisher) = CreatePublisher(waiting);
        using (writer)
        using (publisher)
        {
            await publisher.StartAsync(CancellationToken.None);
            recorder.TryStartTrace("stopping", "GET", "/api/orders", DateTime.UtcNow).ShouldBeTrue();
            (await Task.WhenAny(waiting.Entered.Task, Task.Delay(WaitLimit))).ShouldBe(waiting.Entered.Task);

            var stopwatch = Stopwatch.StartNew();
            await publisher.StopAsync(CancellationToken.None);
            stopwatch.Stop();

            stopwatch.Elapsed.ShouldBeLessThan(WaitLimit);
            publisher.ExecuteTask.ShouldNotBeNull().IsFaulted.ShouldBeFalse();
        }
    }
}

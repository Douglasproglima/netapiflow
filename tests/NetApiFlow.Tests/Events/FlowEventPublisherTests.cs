using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetApiFlow.Events;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Events;

public class FlowEventPublisherTests
{
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
        var recorder = new FlowRecorder(Options.Create(new NetApiFlowOptions()));
        var recording = new RecordingListener();
        var listeners = new IFlowEventListener[] { new ThrowingListener(), recording };
        using var publisher = new FlowEventPublisher(recorder, listeners, NullLogger<FlowEventPublisher>.Instance);

        var started = recorder.TryStartTrace("trace-1", "GET", "/api/orders", DateTime.UtcNow);
        started.ShouldBeTrue();

        var completedTask = await Task.WhenAny(recording.Received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completedTask.ShouldBe(recording.Received.Task);

        var flowEvent = await recording.Received.Task;
        flowEvent.Type.ShouldBe(FlowEventType.TraceStarted);
        flowEvent.TraceId.ShouldBe("trace-1");
    }
}

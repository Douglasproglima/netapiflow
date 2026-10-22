using System.Diagnostics;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;
using NetApiFlow.Marking;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Marking;

public class FlowInterceptorProxyTests
{
    public interface IWorker
    {
        void DoSync();

        int GetSync();

        void ThrowSync();

        Task DoTaskAsync(int delayMs);

        Task<int> GetTaskAsync(int delayMs);

        Task FaultTaskAsync();

        ValueTask DoValueTaskAsync(int delayMs);

        ValueTask<int> GetValueTaskAsync(int delayMs);
    }

    public sealed class Worker : IWorker
    {
        public void DoSync()
        {
        }

        public int GetSync() => 42;

        public void ThrowSync() => throw new InvalidOperationException("sync boom");

        public async Task DoTaskAsync(int delayMs) => await Task.Delay(delayMs);

        public async Task<int> GetTaskAsync(int delayMs)
        {
            await Task.Delay(delayMs);
            return 42;
        }

        public Task FaultTaskAsync() => Task.FromException(new InvalidOperationException("task boom"));

        public async ValueTask DoValueTaskAsync(int delayMs) => await Task.Delay(delayMs);

        public async ValueTask<int> GetValueTaskAsync(int delayMs)
        {
            await Task.Delay(delayMs);
            return 42;
        }
    }

    private static (FlowRecorder Recorder, FlowActivityListener Listener, string TraceId, Activity Root) Arrange()
    {
        var options = Options.Create(new NetApiFlowOptions());
        var recorder = new FlowRecorder(options);
        var listener = new FlowActivityListener(recorder, options);

        var root = new Activity("TestRoot");
        root.Start();
        var traceId = root.TraceId.ToString();
        recorder.TryStartTrace(traceId, "GET", "/api/test", DateTime.UtcNow);

        return (recorder, listener, traceId, root);
    }

    [Fact]
    public void Invoke_SyncVoidMethod_ClosesSpanImmediately()
    {
        var (recorder, listener, traceId, root) = Arrange();
        using var _ = listener;
        using var __ = root;
        var proxy = FlowInterceptorProxy.Create<IWorker, Worker>(new Worker(), ComponentType.Service);

        proxy.DoSync();

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single(s => s.Operation == nameof(IWorker.DoSync));
        span.Status.ShouldBe(FlowStatus.Success);
        span.Component.ShouldBe(nameof(Worker));
        span.DurationMicros.ShouldNotBeNull();
    }

    [Fact]
    public void Invoke_SyncMethodWithReturnValue_ReturnsValueAndClosesSpan()
    {
        var (recorder, listener, traceId, root) = Arrange();
        using var _ = listener;
        using var __ = root;
        var proxy = FlowInterceptorProxy.Create<IWorker, Worker>(new Worker(), ComponentType.Service);

        var result = proxy.GetSync();

        result.ShouldBe(42);
        recorder.GetActiveTrace(traceId)!.Spans.Single(s => s.Operation == nameof(IWorker.GetSync)).Status
            .ShouldBe(FlowStatus.Success);
    }

    [Fact]
    public void Invoke_WhenSyncMethodThrows_MarksSpanAsErrorAndRethrows()
    {
        var (recorder, listener, traceId, root) = Arrange();
        using var _ = listener;
        using var __ = root;
        var proxy = FlowInterceptorProxy.Create<IWorker, Worker>(new Worker(), ComponentType.Service);

        Should.Throw<InvalidOperationException>(() => proxy.ThrowSync());

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single(s => s.Operation == nameof(IWorker.ThrowSync));
        span.Status.ShouldBe(FlowStatus.Error);
        span.IsErrorOrigin.ShouldBeTrue();
    }

    [Fact]
    public async Task Invoke_TaskMethod_ClosesSpanWhenTaskCompletesReflectingRealDelay()
    {
        var (recorder, listener, traceId, root) = Arrange();
        using var _ = listener;
        using var __ = root;
        var proxy = FlowInterceptorProxy.Create<IWorker, Worker>(new Worker(), ComponentType.Service);

        await proxy.DoTaskAsync(50);

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single(s => s.Operation == nameof(IWorker.DoTaskAsync));
        span.Status.ShouldBe(FlowStatus.Success);
        span.DurationMicros.ShouldNotBeNull();
        span.DurationMicros!.Value.ShouldBeGreaterThanOrEqualTo(30_000);
    }

    [Fact]
    public async Task Invoke_TaskOfTMethod_ReturnsValueAndReflectsRealDelay()
    {
        var (recorder, listener, traceId, root) = Arrange();
        using var _ = listener;
        using var __ = root;
        var proxy = FlowInterceptorProxy.Create<IWorker, Worker>(new Worker(), ComponentType.Service);

        var result = await proxy.GetTaskAsync(50);

        result.ShouldBe(42);
        var span = recorder.GetActiveTrace(traceId)!.Spans.Single(s => s.Operation == nameof(IWorker.GetTaskAsync));
        span.DurationMicros!.Value.ShouldBeGreaterThanOrEqualTo(30_000);
    }

    [Fact]
    public async Task Invoke_WhenTaskFaults_MarksSpanAsError()
    {
        var (recorder, listener, traceId, root) = Arrange();
        using var _ = listener;
        using var __ = root;
        var proxy = FlowInterceptorProxy.Create<IWorker, Worker>(new Worker(), ComponentType.Service);

        await Should.ThrowAsync<InvalidOperationException>(() => proxy.FaultTaskAsync());

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single(s => s.Operation == nameof(IWorker.FaultTaskAsync));
        span.Status.ShouldBe(FlowStatus.Error);
    }

    [Fact]
    public async Task Invoke_ValueTaskMethod_ClosesSpanWhenCompletes()
    {
        var (recorder, listener, traceId, root) = Arrange();
        using var _ = listener;
        using var __ = root;
        var proxy = FlowInterceptorProxy.Create<IWorker, Worker>(new Worker(), ComponentType.Service);

        await proxy.DoValueTaskAsync(10);

        var span = recorder.GetActiveTrace(traceId)!.Spans.Single(s => s.Operation == nameof(IWorker.DoValueTaskAsync));
        span.Status.ShouldBe(FlowStatus.Success);
    }

    [Fact]
    public async Task Invoke_ValueTaskOfTMethod_ReturnsValueAndReflectsRealDelay()
    {
        var (recorder, listener, traceId, root) = Arrange();
        using var _ = listener;
        using var __ = root;
        var proxy = FlowInterceptorProxy.Create<IWorker, Worker>(new Worker(), ComponentType.Service);

        var result = await proxy.GetValueTaskAsync(50);

        result.ShouldBe(42);
        var span = recorder.GetActiveTrace(traceId)!.Spans.Single(s => s.Operation == nameof(IWorker.GetValueTaskAsync));
        span.DurationMicros!.Value.ShouldBeGreaterThanOrEqualTo(30_000);
    }
}

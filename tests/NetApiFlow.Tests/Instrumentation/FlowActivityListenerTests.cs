using System.Diagnostics;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Instrumentation;

public class FlowActivityListenerTests
{
    private static (FlowRecorder recorder, FlowActivityListener subject, string traceId, Activity root) Arrange(
        NetApiFlowOptions? options = null)
    {
        var wrappedOptions = Options.Create(options ?? new NetApiFlowOptions());
        var recorder = new FlowRecorder(wrappedOptions);
        var subject = new FlowActivityListener(recorder, wrappedOptions);

        var root = new Activity("TestRoot");
        root.Start();
        var traceId = root.TraceId.ToString();
        recorder.TryStartTrace(traceId, "GET", "/api/test", DateTime.UtcNow);

        return (recorder, subject, traceId, root);
    }

    [Fact]
    public void Sample_WhenTracingIsEnabledAndTraceIsTracked_RecordsSpanWithComponentOperationAndType()
    {
        var (recorder, subject, traceId, root) = Arrange();
        using var _ = subject;
        using var __ = root;

        using (NetApiFlow.Step("StockService", "ReserveAsync", ComponentType.Service))
        {
        }

        var trace = recorder.GetActiveTrace(traceId);
        trace.ShouldNotBeNull();
        trace.Spans.Count.ShouldBe(1);
        var span = trace.Spans[0];
        span.Component.ShouldBe("StockService");
        span.Operation.ShouldBe("ReserveAsync");
        span.Type.ShouldBe(ComponentType.Service);
        span.Status.ShouldBe(FlowStatus.Success);
        span.DurationMicros.ShouldNotBeNull();
    }

    [Fact]
    public void Sample_WhenDisabled_ReturnsNoneAndDoesNotRecordASpan()
    {
        var (recorder, subject, traceId, root) = Arrange(new NetApiFlowOptions { Enabled = false });
        using var _ = subject;
        using var __ = root;

        // Warm up: let the JIT compile Step() once before measuring.
        using (NetApiFlow.Step("Component", "Operation"))
        {
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        using (NetApiFlow.Step("Component", "Operation"))
        {
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        // No Activity, FlowSpan or recorder update happens on this path; the small residual
        // allocation is only the tags collection built before Sample rejects the span.
        (after - before).ShouldBeLessThan(320);
        recorder.GetActiveTrace(traceId)!.Spans.ShouldBeEmpty();
    }

    [Fact]
    public void ActivityStopped_WhenDescendantSpanErrors_MarksAncestorsErrorWithoutIsErrorOrigin()
    {
        var (recorder, subject, traceId, root) = Arrange();
        using var _ = subject;
        using var __ = root;

        using (NetApiFlow.Step("Outer", "Op", ComponentType.Service))
        {
            var inner = NetApiFlow.Step("Inner", "Op", ComponentType.Repository);
            inner.MarkError(new InvalidOperationException("boom"));
            inner.Dispose();
        }

        var spans = recorder.GetActiveTrace(traceId)!.Spans;
        var outerSpan = spans.Single(s => s.Component == "Outer");
        var innerSpan = spans.Single(s => s.Component == "Inner");

        innerSpan.Status.ShouldBe(FlowStatus.Error);
        innerSpan.IsErrorOrigin.ShouldBeTrue();
        outerSpan.Status.ShouldBe(FlowStatus.Error);
        outerSpan.IsErrorOrigin.ShouldBeFalse();
    }
}

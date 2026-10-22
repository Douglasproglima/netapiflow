using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Recording;

public class FlowRecorderTests
{
    private static FlowRecorder CreateSubject(int maxSpansPerTrace = 300)
    {
        var options = new NetApiFlowOptions();
        options.Limits.MaxSpansPerTrace = maxSpansPerTrace;
        return new FlowRecorder(Options.Create(options));
    }

    private static FlowSpan CreateSpan(string traceId, int sequence) => new()
    {
        TraceId = traceId,
        SpanId = $"span-{sequence}",
        Sequence = sequence,
        Component = "Component",
        Operation = "Operation",
        Type = ComponentType.Custom,
        StartedAt = DateTime.UtcNow,
        Status = FlowStatus.Running,
    };

    [Fact]
    public void TryStartTrace_WhenBelowLimit_TracksTrace()
    {
        var subject = CreateSubject();

        var started = subject.TryStartTrace("trace-1", "GET", "/api/orders", DateTime.UtcNow);

        started.ShouldBeTrue();
        subject.ActiveTraceCount.ShouldBe(1);
        subject.DroppedTraceCount.ShouldBe(0);
    }

    [Fact]
    public void TryStartTrace_WhenAtMaxActiveTraces_DropsNewTraceAndIncrementsCounter()
    {
        var subject = CreateSubject();

        for (var i = 0; i < FlowRecorder.MaxActiveTraces; i++)
        {
            subject.TryStartTrace($"trace-{i}", "GET", "/api/orders", DateTime.UtcNow).ShouldBeTrue();
        }

        var overflowed = subject.TryStartTrace("trace-overflow", "GET", "/api/orders", DateTime.UtcNow);

        overflowed.ShouldBeFalse();
        subject.ActiveTraceCount.ShouldBe(FlowRecorder.MaxActiveTraces);
        subject.DroppedTraceCount.ShouldBe(1);
    }

    [Fact]
    public void TryAddSpan_WhenAtMaxSpansPerTrace_DropsSpanAndMarksTraceTruncated()
    {
        const int maxSpans = 3;
        var subject = CreateSubject(maxSpans);
        subject.TryStartTrace("trace-1", "GET", "/api/orders", DateTime.UtcNow);

        for (var i = 0; i < maxSpans; i++)
        {
            subject.TryAddSpan("trace-1", CreateSpan("trace-1", i)).ShouldBeTrue();
        }

        var overflowed = subject.TryAddSpan("trace-1", CreateSpan("trace-1", maxSpans));

        overflowed.ShouldBeFalse();
        subject.DroppedSpanCount.ShouldBe(1);
        var trace = subject.GetActiveTrace("trace-1");
        trace.ShouldNotBeNull();
        trace.Truncated.ShouldBeTrue();
        trace.Spans.Count.ShouldBe(maxSpans);
    }
}

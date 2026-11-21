using Microsoft.Extensions.Options;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Recording;

public class FlowRecorderReservationTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private readonly FlowRecorder _subject = new(Options.Create(new NetApiFlowOptions()));
    private readonly List<string> _events = [];

    public FlowRecorderReservationTests()
    {
        _subject.TraceStarted += trace => _events.Add($"trace-started:{trace.RouteTemplate}");
        _subject.SpanStarted += span => _events.Add($"span-started:{span.SpanId}");
        _subject.SpanCompleted += span => _events.Add($"span-completed:{span.SpanId}");
        _subject.SpanFailed += span => _events.Add($"span-failed:{span.SpanId}");
    }

    private static FlowSpan CreateSpan(string spanId, int sequence, FlowStatus status = FlowStatus.Running) => new()
    {
        TraceId = TraceId,
        SpanId = spanId,
        Sequence = sequence,
        Component = "Component",
        Operation = "Operation",
        Type = ComponentType.Custom,
        StartedAt = DateTime.UtcNow,
        Status = status,
    };

    [Fact]
    public void TryReserveTrace_PublishesNothing_ButCountsAsActive()
    {
        var reserved = _subject.TryReserveTrace(TraceId, "GET", DateTime.UtcNow);

        reserved.ShouldBeTrue();
        _events.ShouldBeEmpty();
        _subject.ActiveTraceCount.ShouldBe(1);
        _subject.GetActiveTraces().ShouldBeEmpty();
    }

    [Fact]
    public void TryAddSpan_OnAReservedTrace_KeepsTheSpanButPublishesNothingYet()
    {
        _subject.TryReserveTrace(TraceId, "GET", DateTime.UtcNow);

        var added = _subject.TryAddSpan(TraceId, CreateSpan("pre-routing", 1));
        _subject.CompleteSpan(TraceId, CreateSpan("pre-routing", 1, FlowStatus.Success));

        added.ShouldBeTrue();
        _events.ShouldBeEmpty();
        _subject.GetActiveTrace(TraceId)!.Spans.ShouldHaveSingleItem().Status.ShouldBe(FlowStatus.Success);
    }

    [Fact]
    public void PublishReserved_PublishesTheTraceThenEachSpanItAlreadyHolds_InTheOrderTheyWereAdded()
    {
        _subject.TryReserveTrace(TraceId, "GET", DateTime.UtcNow);
        _subject.TryAddSpan(TraceId, CreateSpan("done", 1));
        _subject.CompleteSpan(TraceId, CreateSpan("done", 1, FlowStatus.Success));
        _subject.TryAddSpan(TraceId, CreateSpan("failed", 2));
        _subject.CompleteSpan(TraceId, CreateSpan("failed", 2, FlowStatus.Error));
        _subject.TryAddSpan(TraceId, CreateSpan("still-running", 3));

        var published = _subject.PublishReserved(TraceId, "/orders/{id}");

        published.ShouldBeTrue();
        _events.ShouldBe(
        [
            "trace-started:/orders/{id}",
            "span-started:done",
            "span-completed:done",
            "span-started:failed",
            "span-failed:failed",
            "span-started:still-running",
        ]);
        _subject.GetActiveTraces().ShouldHaveSingleItem().RouteTemplate.ShouldBe("/orders/{id}");
    }

    [Fact]
    public void AfterPublishReserved_TheEventsOfASpanComeAsTheyHappen()
    {
        _subject.TryReserveTrace(TraceId, "GET", DateTime.UtcNow);
        _subject.PublishReserved(TraceId, "/orders");
        _events.Clear();

        _subject.TryAddSpan(TraceId, CreateSpan("later", 1));
        _subject.CompleteSpan(TraceId, CreateSpan("later", 1, FlowStatus.Success));

        _events.ShouldBe(["span-started:later", "span-completed:later"]);
    }

    [Fact]
    public void PublishReserved_TwiceOrForATraceThatWasNeverReserved_PublishesOnce()
    {
        _subject.TryReserveTrace(TraceId, "GET", DateTime.UtcNow);

        var first = _subject.PublishReserved(TraceId, "/orders");
        var second = _subject.PublishReserved(TraceId, "/orders");
        var unknown = _subject.PublishReserved("unknown", "/orders");

        first.ShouldBeTrue();
        second.ShouldBeFalse();
        unknown.ShouldBeFalse();
        _events.Count(entry => entry.StartsWith("trace-started", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public void DiscardReserved_RemovesTheTraceWithoutAnyEvent_AndFreesTheCount()
    {
        _subject.TryReserveTrace(TraceId, "GET", DateTime.UtcNow);
        _subject.TryAddSpan(TraceId, CreateSpan("pre-routing", 1));

        _subject.DiscardReserved(TraceId);

        _events.ShouldBeEmpty();
        _subject.ActiveTraceCount.ShouldBe(0);
        _subject.GetActiveTrace(TraceId).ShouldBeNull();
    }

    [Fact]
    public void DiscardReserved_NeverRemovesATraceThatWasPublished()
    {
        _subject.TryReserveTrace(TraceId, "GET", DateTime.UtcNow);
        _subject.PublishReserved(TraceId, "/orders");

        _subject.DiscardReserved(TraceId);

        _subject.ActiveTraceCount.ShouldBe(1);
    }

    [Fact]
    public void TryReserveTrace_ForAnIdAlreadyActive_ReturnsFalseAndKeepsTheFirst()
    {
        _subject.TryReserveTrace(TraceId, "GET", DateTime.UtcNow);

        var second = _subject.TryReserveTrace(TraceId, "POST", DateTime.UtcNow);

        second.ShouldBeFalse();
        _subject.GetActiveTrace(TraceId)!.Method.ShouldBe("GET");
    }

    [Fact]
    public void TryReserveTrace_AtTheLimitOfActiveTraces_ReturnsFalseAndCountsTheDrop()
    {
        for (var index = 0; index < FlowRecorder.MaxActiveTraces; index++)
        {
            _subject.TryReserveTrace($"trace-{index}", "GET", DateTime.UtcNow).ShouldBeTrue();
        }

        var overTheLimit = _subject.TryReserveTrace("one-too-many", "GET", DateTime.UtcNow);

        overTheLimit.ShouldBeFalse();
        _subject.DroppedTraceCount.ShouldBe(1);
    }
}

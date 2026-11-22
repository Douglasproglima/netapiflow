using NetApiFlow.Domain;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

public abstract class AtomicTraceSaveTestsBase
{
    private const string RepeatedSpanId = "span-repeated";

    protected abstract ITraceStore CreateStore();

    [Fact]
    public async Task SaveAsync_WhenTheSpansFailToBeWritten_LeavesNoRowOfTheTrace()
    {
        var store = CreateStore();
        var trace = MakeTrace("atomic-1", spanCount: 3);

        await Should.ThrowAsync<Exception>(() => store.SaveAsync(trace, SpansBreakingThePrimaryKey(trace.TraceId), CancellationToken.None).AsTask());

        (await store.GetAsync(trace.TraceId, CancellationToken.None)).ShouldBeNull();
        (await store.QueryAsync(new TraceQuery(), CancellationToken.None)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task SaveAsync_AfterASaveWhoseSpansFailed_StoresTheSameTraceWholeOnTheNextSave()
    {
        var store = CreateStore();
        var failedAttempt = MakeTrace("atomic-2", spanCount: 3);
        await Should.ThrowAsync<Exception>(() => store.SaveAsync(failedAttempt, SpansBreakingThePrimaryKey(failedAttempt.TraceId), CancellationToken.None).AsTask());

        var wholeTrace = MakeTrace("atomic-2", spanCount: 2);
        await store.SaveAsync(wholeTrace, [MakeSpan(wholeTrace.TraceId, "span-root", 0), MakeSpan(wholeTrace.TraceId, "span-child", 1)], CancellationToken.None);

        var detail = (await store.GetAsync(wholeTrace.TraceId, CancellationToken.None)).ShouldNotBeNull();
        detail.Trace.SpanCount.ShouldBe(2);
        detail.Spans.Select(span => span.SpanId).ShouldBe(["span-root", "span-child"]);
    }

    private static FlowSpan[] SpansBreakingThePrimaryKey(string traceId) =>
    [
        MakeSpan(traceId, "span-root", 0),
        MakeSpan(traceId, RepeatedSpanId, 1),
        MakeSpan(traceId, RepeatedSpanId, 2),
    ];

    private static FlowTrace MakeTrace(string traceId, int spanCount) => new()
    {
        TraceId = traceId,
        Method = "GET",
        RouteTemplate = "/api/orders/{id}",
        HttpStatus = 200,
        Status = FlowStatus.Success,
        StartedAt = DateTime.UtcNow,
        EndedAt = DateTime.UtcNow.AddMilliseconds(1),
        DurationMicros = 1_000,
        SpanCount = spanCount,
        Truncated = false,
        ErrorMessage = null,
        Tags = new Dictionary<string, string>(),
    };

    private static FlowSpan MakeSpan(string traceId, string spanId, int sequence) => new()
    {
        TraceId = traceId,
        SpanId = spanId,
        ParentSpanId = null,
        Sequence = sequence,
        Component = "OrderRepository",
        Operation = "FindAsync",
        Type = ComponentType.Repository,
        StartedAt = DateTime.UtcNow,
        EndedAt = DateTime.UtcNow.AddMilliseconds(1),
        DurationMicros = 1_000,
        Status = FlowStatus.Success,
        ErrorType = null,
        ErrorMessage = null,
        IsErrorOrigin = false,
        Tags = new Dictionary<string, string>(),
        Logs = [],
    };
}

using NetApiFlow.Domain;
using NetApiFlow.Storage;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Storage;

public class InMemoryTraceStoreTests
{
    private readonly InMemoryTraceStore _subject = new();

    private static FlowTrace CreateTrace(string traceId, DateTime startedAt) => new()
    {
        TraceId = traceId,
        Method = "GET",
        RouteTemplate = "/api/orders",
        Status = FlowStatus.Success,
        StartedAt = startedAt,
    };

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ReturnsTheSameTrace()
    {
        var trace = CreateTrace("trace-1", DateTime.UtcNow);

        await _subject.SaveAsync(trace, [], CancellationToken.None);
        var result = await _subject.GetAsync("trace-1", CancellationToken.None);

        result.ShouldNotBeNull();
        result.Trace.ShouldBe(trace);
        result.Spans.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyRetentionAsync_RemovesOnlyTracesOlderThanMaxAge()
    {
        var old = CreateTrace("old", DateTime.UtcNow - TimeSpan.FromDays(2));
        var recent = CreateTrace("recent", DateTime.UtcNow);
        await _subject.SaveAsync(old, [], CancellationToken.None);
        await _subject.SaveAsync(recent, [], CancellationToken.None);

        var removed = await _subject.ApplyRetentionAsync(
            new RetentionPolicy { MaxTraces = 1000, MaxAge = TimeSpan.FromHours(24) },
            CancellationToken.None);

        removed.ShouldBe(1);
        (await _subject.GetAsync("old", CancellationToken.None)).ShouldBeNull();
        (await _subject.GetAsync("recent", CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task ApplyRetentionAsync_RemovesOldestFirstWhenOverMaxTraces()
    {
        var baseTime = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            await _subject.SaveAsync(CreateTrace($"trace-{i}", baseTime.AddSeconds(i)), [], CancellationToken.None);
        }

        var removed = await _subject.ApplyRetentionAsync(
            new RetentionPolicy { MaxTraces = 3, MaxAge = TimeSpan.FromDays(365) },
            CancellationToken.None);

        removed.ShouldBe(2);
        (await _subject.GetAsync("trace-0", CancellationToken.None)).ShouldBeNull();
        (await _subject.GetAsync("trace-1", CancellationToken.None)).ShouldBeNull();
        (await _subject.GetAsync("trace-4", CancellationToken.None)).ShouldNotBeNull();
    }
}

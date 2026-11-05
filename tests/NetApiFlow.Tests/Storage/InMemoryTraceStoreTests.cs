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

    [Fact]
    public async Task GetRouteStatsAsync_GroupsByMethodAndRouteTemplate()
    {
        await _subject.SaveAsync(new FlowTrace
        {
            TraceId = "orders-get-1",
            Method = "GET",
            RouteTemplate = "/api/orders/{id}",
            Status = FlowStatus.Success,
            StartedAt = DateTime.UtcNow,
            DurationMicros = 1000,
        }, [], CancellationToken.None);
        await _subject.SaveAsync(new FlowTrace
        {
            TraceId = "customers-get-1",
            Method = "GET",
            RouteTemplate = "/api/customers",
            Status = FlowStatus.Success,
            StartedAt = DateTime.UtcNow,
            DurationMicros = 2000,
        }, [], CancellationToken.None);

        var stats = await _subject.GetRouteStatsAsync(new StatsQuery(), CancellationToken.None);

        stats.Count.ShouldBe(2);
        stats.ShouldContain(s => s.Method == "GET" && s.RouteTemplate == "/api/orders/{id}" && s.ExecutionCount == 1);
        stats.ShouldContain(s => s.Method == "GET" && s.RouteTemplate == "/api/customers" && s.ExecutionCount == 1);
    }

    [Fact]
    public async Task GetRouteStatsAsync_SameRouteDifferentMethod_TreatedAsSeparateEntries()
    {
        await _subject.SaveAsync(new FlowTrace
        {
            TraceId = "orders-get-1",
            Method = "GET",
            RouteTemplate = "/api/orders/{id}",
            Status = FlowStatus.Success,
            StartedAt = DateTime.UtcNow,
            DurationMicros = 1000,
        }, [], CancellationToken.None);
        await _subject.SaveAsync(new FlowTrace
        {
            TraceId = "orders-delete-1",
            Method = "DELETE",
            RouteTemplate = "/api/orders/{id}",
            Status = FlowStatus.Success,
            StartedAt = DateTime.UtcNow,
            DurationMicros = 500,
        }, [], CancellationToken.None);

        var stats = await _subject.GetRouteStatsAsync(new StatsQuery(), CancellationToken.None);

        stats.Count.ShouldBe(2);
        stats.ShouldContain(s => s.Method == "GET" && s.RouteTemplate == "/api/orders/{id}");
        stats.ShouldContain(s => s.Method == "DELETE" && s.RouteTemplate == "/api/orders/{id}");
    }

    [Fact]
    public async Task GetRouteStatsAsync_FiltersByFromAndTo()
    {
        var baseTime = DateTime.UtcNow;
        await _subject.SaveAsync(new FlowTrace
        {
            TraceId = "outside-window",
            Method = "GET",
            RouteTemplate = "/api/orders",
            Status = FlowStatus.Success,
            StartedAt = baseTime - TimeSpan.FromDays(10),
            DurationMicros = 1000,
        }, [], CancellationToken.None);
        await _subject.SaveAsync(new FlowTrace
        {
            TraceId = "inside-window",
            Method = "GET",
            RouteTemplate = "/api/orders",
            Status = FlowStatus.Success,
            StartedAt = baseTime,
            DurationMicros = 1000,
        }, [], CancellationToken.None);

        var stats = await _subject.GetRouteStatsAsync(
            new StatsQuery { From = baseTime - TimeSpan.FromHours(1), To = baseTime + TimeSpan.FromHours(1) },
            CancellationToken.None);

        stats.Count.ShouldBe(1);
        stats[0].ExecutionCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetRouteStatsAsync_RunningTraceWithNullDuration_CountedButExcludedFromDurationStats()
    {
        await _subject.SaveAsync(new FlowTrace
        {
            TraceId = "running",
            Method = "GET",
            RouteTemplate = "/api/orders",
            Status = FlowStatus.Running,
            StartedAt = DateTime.UtcNow,
            DurationMicros = null,
        }, [], CancellationToken.None);

        var stats = await _subject.GetRouteStatsAsync(new StatsQuery(), CancellationToken.None);

        stats.Count.ShouldBe(1);
        stats[0].ExecutionCount.ShouldBe(1);
        stats[0].MedianDurationMicros.ShouldBe(0);
        stats[0].AverageDurationMicros.ShouldBe(0);
        stats[0].P95DurationMicros.ShouldBe(0);
        stats[0].MaxDurationMicros.ShouldBe(0);
    }
}

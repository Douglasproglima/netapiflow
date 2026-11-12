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

    private static FlowSpan CreateSpan(string traceId, string spanId, string? parentSpanId, int sequence, string component, string operation, ComponentType type = ComponentType.Service) => new()
    {
        TraceId = traceId,
        SpanId = spanId,
        ParentSpanId = parentSpanId,
        Sequence = sequence,
        Component = component,
        Operation = operation,
        Type = type,
        StartedAt = DateTime.UtcNow,
        Status = FlowStatus.Success,
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
    public async Task GetRouteStatsAsync_SameMethodDifferentCasing_TreatedAsOneEntryWithUppercaseMethod()
    {
        await _subject.SaveAsync(new FlowTrace
        {
            TraceId = "orders-get-upper",
            Method = "GET",
            RouteTemplate = "/api/orders/{id}",
            Status = FlowStatus.Success,
            StartedAt = DateTime.UtcNow,
            DurationMicros = 1000,
        }, [], CancellationToken.None);
        await _subject.SaveAsync(new FlowTrace
        {
            TraceId = "orders-get-lower",
            Method = "get",
            RouteTemplate = "/api/orders/{id}",
            Status = FlowStatus.Success,
            StartedAt = DateTime.UtcNow,
            DurationMicros = 1000,
        }, [], CancellationToken.None);

        var stats = await _subject.GetRouteStatsAsync(new StatsQuery(), CancellationToken.None);

        stats.Count.ShouldBe(1);
        stats[0].Method.ShouldBe("GET");
        stats[0].ExecutionCount.ShouldBe(2);
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

    [Fact]
    public async Task GetArchitectureMapAsync_SingleTrace_BuildsSimpleGraphWithCountsOfOne()
    {
        var trace = CreateTrace("arch-1", DateTime.UtcNow);
        var spans = new[]
        {
            CreateSpan("arch-1", "root", null, 0, "OrdersController", "GetOrder", ComponentType.Controller),
            CreateSpan("arch-1", "service", "root", 1, "OrderService", "FindAsync"),
            CreateSpan("arch-1", "repository", "service", 2, "OrderRepository", "FindAsync", ComponentType.Repository),
        };
        await _subject.SaveAsync(trace, spans, CancellationToken.None);

        var map = await _subject.GetArchitectureMapAsync("GET", "/api/orders", new StatsQuery(), CancellationToken.None);

        map.TotalExecutions.ShouldBe(1);
        map.Nodes.Count.ShouldBe(3);
        map.Nodes.ShouldAllBe(n => n.ExecutionCount == 1);
        map.Nodes.ShouldContain(n => n.Component == "OrdersController" && n.Operation == "GetOrder" && n.Type == ComponentType.Controller);
        map.Edges.Count.ShouldBe(2);
        map.Edges.ShouldAllBe(e => e.CallCount == 1);
        map.Edges.ShouldContain(e => e.FromComponent == "OrdersController" && e.ToComponent == "OrderService");
        map.Edges.ShouldContain(e => e.FromComponent == "OrderService" && e.ToComponent == "OrderRepository");
    }

    [Fact]
    public async Task GetArchitectureMapAsync_TwoExecutionsSamePath_DoublesCountsWithoutDuplicatingNodesOrEdges()
    {
        for (var i = 0; i < 2; i++)
        {
            var traceId = $"arch-same-{i}";
            await _subject.SaveAsync(CreateTrace(traceId, DateTime.UtcNow), new[]
            {
                CreateSpan(traceId, "root", null, 0, "OrdersController", "GetOrder", ComponentType.Controller),
                CreateSpan(traceId, "service", "root", 1, "OrderService", "FindAsync"),
            }, CancellationToken.None);
        }

        var map = await _subject.GetArchitectureMapAsync("GET", "/api/orders", new StatsQuery(), CancellationToken.None);

        map.TotalExecutions.ShouldBe(2);
        map.Nodes.Count.ShouldBe(2);
        map.Nodes.ShouldAllBe(n => n.ExecutionCount == 2);
        map.Edges.ShouldHaveSingleItem().CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetArchitectureMapAsync_DifferentPaths_NonSharedStepsHaveCountsBelowTotalExecutions()
    {
        await _subject.SaveAsync(CreateTrace("arch-cache-hit", DateTime.UtcNow), new[]
        {
            CreateSpan("arch-cache-hit", "root", null, 0, "OrdersController", "GetOrder", ComponentType.Controller),
            CreateSpan("arch-cache-hit", "cache", "root", 1, "OrderCache", "TryGetAsync"),
        }, CancellationToken.None);
        await _subject.SaveAsync(CreateTrace("arch-cache-miss", DateTime.UtcNow), new[]
        {
            CreateSpan("arch-cache-miss", "root", null, 0, "OrdersController", "GetOrder", ComponentType.Controller),
            CreateSpan("arch-cache-miss", "repository", "root", 1, "OrderRepository", "FindAsync", ComponentType.Repository),
        }, CancellationToken.None);

        var map = await _subject.GetArchitectureMapAsync("GET", "/api/orders", new StatsQuery(), CancellationToken.None);

        map.TotalExecutions.ShouldBe(2);
        map.Nodes.ShouldContain(n => n.Component == "OrdersController" && n.ExecutionCount == 2);
        map.Nodes.ShouldContain(n => n.Component == "OrderCache" && n.ExecutionCount == 1);
        map.Nodes.ShouldContain(n => n.Component == "OrderRepository" && n.ExecutionCount == 1);
    }

    [Fact]
    public async Task GetArchitectureMapAsync_RouteWithNoExecutionsInWindow_ReturnsZeroAndEmptyLists()
    {
        await _subject.SaveAsync(CreateTrace("arch-other-route", DateTime.UtcNow), [], CancellationToken.None);

        var map = await _subject.GetArchitectureMapAsync("GET", "/api/never-called", new StatsQuery(), CancellationToken.None);

        map.TotalExecutions.ShouldBe(0);
        map.Nodes.ShouldBeEmpty();
        map.Edges.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetArchitectureMapAsync_FiltersByFromAndTo()
    {
        var baseTime = DateTime.UtcNow;
        await _subject.SaveAsync(CreateTrace("arch-outside", baseTime - TimeSpan.FromDays(10)), new[]
        {
            CreateSpan("arch-outside", "root", null, 0, "OrdersController", "GetOrder", ComponentType.Controller),
        }, CancellationToken.None);
        await _subject.SaveAsync(CreateTrace("arch-inside", baseTime), new[]
        {
            CreateSpan("arch-inside", "root", null, 0, "OrdersController", "GetOrder", ComponentType.Controller),
        }, CancellationToken.None);

        var map = await _subject.GetArchitectureMapAsync(
            "GET", "/api/orders",
            new StatsQuery { From = baseTime - TimeSpan.FromHours(1), To = baseTime + TimeSpan.FromHours(1) },
            CancellationToken.None);

        map.TotalExecutions.ShouldBe(1);
    }

    [Fact]
    public async Task GetArchitectureMapAsync_SameStepCalledRepeatedlyInOneTrace_NodeCountsTraceOnceEdgeCountsEveryCall()
    {
        await _subject.SaveAsync(CreateTrace("arch-loop", DateTime.UtcNow), new[]
        {
            CreateSpan("arch-loop", "root", null, 0, "OrdersController", "GetOrder", ComponentType.Controller),
            CreateSpan("arch-loop", "r1", "root", 1, "OrderRepository", "FindAsync", ComponentType.Repository),
            CreateSpan("arch-loop", "r2", "root", 2, "OrderRepository", "FindAsync", ComponentType.Repository),
            CreateSpan("arch-loop", "r3", "root", 3, "OrderRepository", "FindAsync", ComponentType.Repository),
        }, CancellationToken.None);

        var map = await _subject.GetArchitectureMapAsync("GET", "/api/orders", new StatsQuery(), CancellationToken.None);

        map.Nodes.ShouldContain(n => n.Component == "OrderRepository" && n.ExecutionCount == 1);
        map.Edges.ShouldContain(e => e.FromComponent == "OrdersController" && e.ToComponent == "OrderRepository" && e.CallCount == 3);
    }
}

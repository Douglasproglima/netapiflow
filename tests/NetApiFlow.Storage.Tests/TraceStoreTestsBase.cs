using NetApiFlow.Domain;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

/// <summary>
/// Exercises the full <see cref="ITraceStore"/> contract against a real database. Each test calls
/// <see cref="CreateStore"/>, which gives it a fresh, uniquely-prefixed set of tables on the same
/// shared container (tarefa 6.5): tests never see each other's rows, without dropping tables
/// between them.
/// </summary>
public abstract class TraceStoreTestsBase
{
    protected abstract ITraceStore CreateStore();

    private static FlowTrace MakeTrace(string traceId, string method = "GET", string route = "/api/orders/{id}", FlowStatus status = FlowStatus.Success, DateTime? startedAt = null, long? durationMicros = 1_000, string? tenant = null) => new()
    {
        TraceId = traceId,
        Method = method,
        RouteTemplate = route,
        HttpStatus = status == FlowStatus.Error ? 500 : 200,
        Status = status,
        StartedAt = startedAt ?? DateTime.UtcNow,
        EndedAt = status == FlowStatus.Running ? null : (startedAt ?? DateTime.UtcNow).AddMicroseconds(durationMicros ?? 0),
        DurationMicros = status == FlowStatus.Running ? null : durationMicros,
        SpanCount = 1,
        Truncated = false,
        ErrorMessage = status == FlowStatus.Error ? "boom" : null,
        Tags = tenant is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["tenant"] = tenant },
    };

    private static FlowSpan MakeSpan(string traceId, string spanId, string? parentSpanId, int sequence, string component = "OrderRepository", string operation = "FindAsync", long durationMicros = 1_000, FlowStatus status = FlowStatus.Success) => new()
    {
        TraceId = traceId,
        SpanId = spanId,
        ParentSpanId = parentSpanId,
        Sequence = sequence,
        Component = component,
        Operation = operation,
        Type = ComponentType.Repository,
        StartedAt = DateTime.UtcNow,
        EndedAt = DateTime.UtcNow.AddMicroseconds(durationMicros),
        DurationMicros = durationMicros,
        Status = status,
        ErrorType = status == FlowStatus.Error ? "System.InvalidOperationException" : null,
        ErrorMessage = status == FlowStatus.Error ? "boom" : null,
        IsErrorOrigin = status == FlowStatus.Error,
        Tags = new Dictionary<string, string> { ["key"] = "value" },
    };

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTripsTraceAndSpans()
    {
        var store = CreateStore();
        var trace = MakeTrace("trace-1", tenant: "acme");
        var root = MakeSpan("trace-1", "span-root", null, 0);
        var child = MakeSpan("trace-1", "span-child", "span-root", 1, component: "Database", operation: "SELECT orders");
        await store.SaveAsync(trace, [root, child], CancellationToken.None);

        var detail = await store.GetAsync("trace-1", CancellationToken.None);

        detail.ShouldNotBeNull();
        detail.Trace.TraceId.ShouldBe("trace-1");
        detail.Trace.Tags["tenant"].ShouldBe("acme");
        detail.Spans.Count.ShouldBe(2);
        detail.Spans[0].SpanId.ShouldBe("span-root");
        detail.Spans[1].ParentSpanId.ShouldBe("span-root");
        detail.Spans[1].Tags["key"].ShouldBe("value");
    }

    [Fact]
    public async Task GetAsync_UnknownTraceId_ReturnsNull()
    {
        var store = CreateStore();

        var detail = await store.GetAsync("does-not-exist", CancellationToken.None);

        detail.ShouldBeNull();
    }

    [Fact]
    public async Task QueryAsync_FiltersByEveryFieldAndPaginates()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeTrace("q-1", method: "GET", route: "/api/orders", status: FlowStatus.Success, startedAt: now.AddMinutes(-10), durationMicros: 1_000, tenant: "acme"), [], CancellationToken.None);
        await store.SaveAsync(MakeTrace("q-2", method: "POST", route: "/api/orders", status: FlowStatus.Error, startedAt: now.AddMinutes(-5), durationMicros: 5_000, tenant: "acme"), [], CancellationToken.None);
        await store.SaveAsync(MakeTrace("q-3", method: "GET", route: "/api/users", status: FlowStatus.Success, startedAt: now.AddMinutes(-1), durationMicros: 2_000, tenant: "other"), [], CancellationToken.None);

        var byStatus = await store.QueryAsync(new TraceQuery { Status = FlowStatus.Error }, CancellationToken.None);
        byStatus.Items.Select(i => i.TraceId).ShouldBe(["q-2"]);

        var byMethod = await store.QueryAsync(new TraceQuery { Method = "post" }, CancellationToken.None);
        byMethod.Items.Select(i => i.TraceId).ShouldBe(["q-2"]);

        var byRoute = await store.QueryAsync(new TraceQuery { RouteTemplate = "/api/users" }, CancellationToken.None);
        byRoute.Items.Select(i => i.TraceId).ShouldBe(["q-3"]);

        var byTenant = await store.QueryAsync(new TraceQuery { Tenant = "other" }, CancellationToken.None);
        byTenant.Items.Select(i => i.TraceId).ShouldBe(["q-3"]);

        var byMinDuration = await store.QueryAsync(new TraceQuery { MinDurationMicros = 2_000 }, CancellationToken.None);
        byMinDuration.Items.Select(i => i.TraceId).ShouldBe(["q-3", "q-2"]);

        var byWindow = await store.QueryAsync(new TraceQuery { From = now.AddMinutes(-6), To = now.AddMinutes(-2) }, CancellationToken.None);
        byWindow.Items.Select(i => i.TraceId).ShouldBe(["q-2"]);

        var firstPage = await store.QueryAsync(new TraceQuery { Page = 1, PageSize = 2 }, CancellationToken.None);
        firstPage.TotalCount.ShouldBe(3);
        firstPage.Items.Count.ShouldBe(2);
        firstPage.Items.Select(i => i.TraceId).ShouldBe(["q-3", "q-2"]);

        var secondPage = await store.QueryAsync(new TraceQuery { Page = 2, PageSize = 2 }, CancellationToken.None);
        secondPage.Items.Select(i => i.TraceId).ShouldBe(["q-1"]);
    }

    [Fact]
    public async Task GetComponentStatsAsync_AggregatesPerComponentAndOperation()
    {
        var store = CreateStore();
        var trace = MakeTrace("stats-1");
        var spans = new[]
        {
            MakeSpan("stats-1", "s1", null, 0, component: "OrderRepository", operation: "FindAsync", durationMicros: 1_000),
            MakeSpan("stats-1", "s2", "s1", 1, component: "OrderRepository", operation: "FindAsync", durationMicros: 3_000),
            MakeSpan("stats-1", "s3", "s1", 2, component: "OrderRepository", operation: "FindAsync", durationMicros: 2_000, status: FlowStatus.Error),
        };
        await store.SaveAsync(trace, spans, CancellationToken.None);

        var stats = await store.GetComponentStatsAsync(new StatsQuery(), CancellationToken.None);

        var stat = stats.ShouldHaveSingleItem();
        stat.Component.ShouldBe("OrderRepository");
        stat.Operation.ShouldBe("FindAsync");
        stat.ExecutionCount.ShouldBe(3);
        stat.ErrorCount.ShouldBe(1);
        stat.MedianDurationMicros.ShouldBe(2_000);
        stat.MaxDurationMicros.ShouldBe(3_000);
    }

    [Fact]
    public async Task GetTraceStatsAsync_AggregatesAcrossWholeTraces()
    {
        var store = CreateStore();
        await store.SaveAsync(MakeTrace("ts-1", status: FlowStatus.Success, durationMicros: 1_000), [], CancellationToken.None);
        await store.SaveAsync(MakeTrace("ts-2", status: FlowStatus.Error, durationMicros: 3_000), [], CancellationToken.None);

        var stats = await store.GetTraceStatsAsync(new StatsQuery(), CancellationToken.None);

        stats.TotalTraces.ShouldBe(2);
        stats.ErrorCount.ShouldBe(1);
        stats.MaxDurationMicros.ShouldBe(3_000);
    }

    [Fact]
    public async Task GetRouteStatsAsync_AggregatesPerMethodAndRouteTemplate()
    {
        var store = CreateStore();
        await store.SaveAsync(MakeTrace("rs-1", method: "GET", route: "/api/orders/{id}", durationMicros: 1_000), [], CancellationToken.None);
        await store.SaveAsync(MakeTrace("rs-2", method: "GET", route: "/api/orders/{id}", durationMicros: 3_000), [], CancellationToken.None);
        await store.SaveAsync(MakeTrace("rs-3", method: "GET", route: "/api/orders/{id}", durationMicros: 2_000, status: FlowStatus.Error), [], CancellationToken.None);
        await store.SaveAsync(MakeTrace("rs-4", method: "GET", route: "/api/customers", durationMicros: 500), [], CancellationToken.None);

        var stats = await store.GetRouteStatsAsync(new StatsQuery(), CancellationToken.None);

        stats.Count.ShouldBe(2);
        var orders = stats.Single(s => s.RouteTemplate == "/api/orders/{id}");
        orders.Method.ShouldBe("GET");
        orders.ExecutionCount.ShouldBe(3);
        orders.ErrorCount.ShouldBe(1);
        orders.MedianDurationMicros.ShouldBe(2_000);
        orders.MaxDurationMicros.ShouldBe(3_000);
    }

    [Fact]
    public async Task GetRouteStatsAsync_SameRouteDifferentMethod_TreatedAsSeparateEntries()
    {
        var store = CreateStore();
        await store.SaveAsync(MakeTrace("rs-get", method: "GET", route: "/api/orders/{id}"), [], CancellationToken.None);
        await store.SaveAsync(MakeTrace("rs-delete", method: "DELETE", route: "/api/orders/{id}"), [], CancellationToken.None);

        var stats = await store.GetRouteStatsAsync(new StatsQuery(), CancellationToken.None);

        stats.Count.ShouldBe(2);
        stats.ShouldContain(s => s.Method == "GET" && s.RouteTemplate == "/api/orders/{id}");
        stats.ShouldContain(s => s.Method == "DELETE" && s.RouteTemplate == "/api/orders/{id}");
    }

    [Fact]
    public async Task GetRouteStatsAsync_FiltersByFromAndTo()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeTrace("rs-old", route: "/api/orders", startedAt: now.AddDays(-10)), [], CancellationToken.None);
        await store.SaveAsync(MakeTrace("rs-new", route: "/api/orders", startedAt: now), [], CancellationToken.None);

        var stats = await store.GetRouteStatsAsync(
            new StatsQuery { From = now.AddHours(-1), To = now.AddHours(1) },
            CancellationToken.None);

        var stat = stats.ShouldHaveSingleItem();
        stat.ExecutionCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetRouteStatsAsync_RunningTraceWithNullDuration_CountedButExcludedFromDurationStats()
    {
        var store = CreateStore();
        await store.SaveAsync(MakeRawTrace("rs-running", DateTime.UtcNow, httpStatus: null, durationMicros: null), [], CancellationToken.None);

        var stats = await store.GetRouteStatsAsync(new StatsQuery(), CancellationToken.None);

        var stat = stats.ShouldHaveSingleItem();
        stat.ExecutionCount.ShouldBe(1);
        stat.MedianDurationMicros.ShouldBe(0);
        stat.MaxDurationMicros.ShouldBe(0);
    }

    [Fact]
    public async Task GetArchitectureMapAsync_SingleTrace_BuildsSimpleGraphWithCountsOfOne()
    {
        var store = CreateStore();
        var trace = MakeTrace("arch-1", route: "/api/orders/{id}");
        var spans = new[]
        {
            MakeSpan("arch-1", "root", null, 0, component: "OrdersController", operation: "GetOrder"),
            MakeSpan("arch-1", "service", "root", 1, component: "OrderService", operation: "FindAsync"),
            MakeSpan("arch-1", "repository", "service", 2, component: "OrderRepository", operation: "FindAsync"),
        };
        await store.SaveAsync(trace, spans, CancellationToken.None);

        var map = await store.GetArchitectureMapAsync("GET", "/api/orders/{id}", new StatsQuery(), CancellationToken.None);

        map.TotalExecutions.ShouldBe(1);
        map.Nodes.Count.ShouldBe(3);
        map.Nodes.ShouldAllBe(n => n.ExecutionCount == 1);
        map.Edges.Count.ShouldBe(2);
        map.Edges.ShouldAllBe(e => e.CallCount == 1);
        map.Edges.ShouldContain(e => e.FromComponent == "OrdersController" && e.ToComponent == "OrderService");
        map.Edges.ShouldContain(e => e.FromComponent == "OrderService" && e.ToComponent == "OrderRepository");
    }

    [Fact]
    public async Task GetArchitectureMapAsync_TwoExecutionsSamePath_DoublesCountsWithoutDuplicatingNodesOrEdges()
    {
        var store = CreateStore();
        for (var i = 0; i < 2; i++)
        {
            var traceId = $"arch-same-{i}";
            await store.SaveAsync(MakeTrace(traceId, route: "/api/orders/{id}"), new[]
            {
                MakeSpan(traceId, "root", null, 0, component: "OrdersController", operation: "GetOrder"),
                MakeSpan(traceId, "service", "root", 1, component: "OrderService", operation: "FindAsync"),
            }, CancellationToken.None);
        }

        var map = await store.GetArchitectureMapAsync("GET", "/api/orders/{id}", new StatsQuery(), CancellationToken.None);

        map.TotalExecutions.ShouldBe(2);
        map.Nodes.Count.ShouldBe(2);
        map.Nodes.ShouldAllBe(n => n.ExecutionCount == 2);
        map.Edges.ShouldHaveSingleItem().CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetArchitectureMapAsync_DifferentPaths_NonSharedStepsHaveCountsBelowTotalExecutions()
    {
        var store = CreateStore();
        await store.SaveAsync(MakeTrace("arch-cache-hit", route: "/api/orders/{id}"), new[]
        {
            MakeSpan("arch-cache-hit", "root", null, 0, component: "OrdersController", operation: "GetOrder"),
            MakeSpan("arch-cache-hit", "cache", "root", 1, component: "OrderCache", operation: "TryGetAsync"),
        }, CancellationToken.None);
        await store.SaveAsync(MakeTrace("arch-cache-miss", route: "/api/orders/{id}"), new[]
        {
            MakeSpan("arch-cache-miss", "root", null, 0, component: "OrdersController", operation: "GetOrder"),
            MakeSpan("arch-cache-miss", "repo", "root", 1, component: "OrderRepository", operation: "FindAsync"),
        }, CancellationToken.None);

        var map = await store.GetArchitectureMapAsync("GET", "/api/orders/{id}", new StatsQuery(), CancellationToken.None);

        map.TotalExecutions.ShouldBe(2);
        map.Nodes.ShouldContain(n => n.Component == "OrdersController" && n.ExecutionCount == 2);
        map.Nodes.ShouldContain(n => n.Component == "OrderCache" && n.ExecutionCount == 1);
        map.Nodes.ShouldContain(n => n.Component == "OrderRepository" && n.ExecutionCount == 1);
    }

    [Fact]
    public async Task GetArchitectureMapAsync_RouteWithNoExecutionsInWindow_ReturnsZeroAndEmptyLists()
    {
        var store = CreateStore();
        await store.SaveAsync(MakeTrace("arch-other-route", route: "/api/orders/{id}"), [], CancellationToken.None);

        var map = await store.GetArchitectureMapAsync("GET", "/api/never-called", new StatsQuery(), CancellationToken.None);

        map.TotalExecutions.ShouldBe(0);
        map.Nodes.ShouldBeEmpty();
        map.Edges.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetArchitectureMapAsync_FiltersByFromAndTo()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeTrace("arch-outside", route: "/api/orders/{id}", startedAt: now.AddDays(-10)), new[]
        {
            MakeSpan("arch-outside", "root", null, 0, component: "OrdersController", operation: "GetOrder"),
        }, CancellationToken.None);
        await store.SaveAsync(MakeTrace("arch-inside", route: "/api/orders/{id}", startedAt: now), new[]
        {
            MakeSpan("arch-inside", "root", null, 0, component: "OrdersController", operation: "GetOrder"),
        }, CancellationToken.None);

        var map = await store.GetArchitectureMapAsync(
            "GET", "/api/orders/{id}",
            new StatsQuery { From = now.AddHours(-1), To = now.AddHours(1) },
            CancellationToken.None);

        map.TotalExecutions.ShouldBe(1);
    }

    [Fact]
    public async Task GetArchitectureMapAsync_SameStepCalledRepeatedlyInOneTrace_NodeCountsTraceOnceEdgeCountsEveryCall()
    {
        var store = CreateStore();
        await store.SaveAsync(MakeTrace("arch-loop", route: "/api/orders/{id}"), new[]
        {
            MakeSpan("arch-loop", "root", null, 0, component: "OrdersController", operation: "GetOrder"),
            MakeSpan("arch-loop", "r1", "root", 1, component: "OrderRepository", operation: "FindAsync"),
            MakeSpan("arch-loop", "r2", "root", 2, component: "OrderRepository", operation: "FindAsync"),
            MakeSpan("arch-loop", "r3", "root", 3, component: "OrderRepository", operation: "FindAsync"),
        }, CancellationToken.None);

        var map = await store.GetArchitectureMapAsync("GET", "/api/orders/{id}", new StatsQuery(), CancellationToken.None);

        map.Nodes.ShouldContain(n => n.Component == "OrderRepository" && n.ExecutionCount == 1);
        map.Edges.ShouldContain(e => e.FromComponent == "OrdersController" && e.ToComponent == "OrderRepository" && e.CallCount == 3);
    }

    [Fact]
    public async Task GetLastSuccessfulAsync_ReturnsMostRecentSuccessForThatEndpoint()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeTrace("ls-1", method: "GET", route: "/api/orders/{id}", status: FlowStatus.Success, startedAt: now.AddMinutes(-10)), [MakeSpan("ls-1", "s1", null, 0)], CancellationToken.None);
        await store.SaveAsync(MakeTrace("ls-2", method: "GET", route: "/api/orders/{id}", status: FlowStatus.Error, startedAt: now.AddMinutes(-5)), [], CancellationToken.None);
        await store.SaveAsync(MakeTrace("ls-3", method: "GET", route: "/api/orders/{id}", status: FlowStatus.Success, startedAt: now.AddMinutes(-1)), [MakeSpan("ls-3", "s1", null, 0)], CancellationToken.None);
        await store.SaveAsync(MakeTrace("ls-4", method: "POST", route: "/api/orders/{id}", status: FlowStatus.Success, startedAt: now), [], CancellationToken.None);

        var blueprint = await store.GetLastSuccessfulAsync("GET", "/api/orders/{id}", CancellationToken.None);

        blueprint.ShouldNotBeNull();
        blueprint.Trace.TraceId.ShouldBe("ls-3");
        blueprint.Spans.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetLastSuccessfulAsync_NoSuccessForThatEndpoint_ReturnsNull()
    {
        var store = CreateStore();

        var blueprint = await store.GetLastSuccessfulAsync("GET", "/api/never-called", CancellationToken.None);

        blueprint.ShouldBeNull();
    }

    [Fact]
    public async Task ApplyRetentionAsync_RemovesOnlyTracesBeyondThePolicyAndTheirSpans()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeTrace("old-1", startedAt: now.AddDays(-2)), [MakeSpan("old-1", "s1", null, 0)], CancellationToken.None);
        await store.SaveAsync(MakeTrace("new-1", startedAt: now.AddMinutes(-1)), [MakeSpan("new-1", "s1", null, 0)], CancellationToken.None);
        await store.SaveAsync(MakeTrace("new-2", startedAt: now), [MakeSpan("new-2", "s1", null, 0)], CancellationToken.None);

        var removed = await store.ApplyRetentionAsync(new RetentionPolicy { MaxTraces = 100, MaxAge = TimeSpan.FromDays(1) }, CancellationToken.None);

        removed.ShouldBe(1);
        (await store.GetAsync("old-1", CancellationToken.None)).ShouldBeNull();
        (await store.GetAsync("new-1", CancellationToken.None)).ShouldNotBeNull();
        (await store.GetAsync("new-2", CancellationToken.None)).ShouldNotBeNull();

        // No orphaned spans: aggregating per component/operation never sees the removed trace's spans.
        var stats = await store.GetComponentStatsAsync(new StatsQuery(), CancellationToken.None);
        stats.Sum(s => s.ExecutionCount).ShouldBe(2);
    }

    private static FlowTrace MakeRawTrace(string traceId, DateTime startedAt, string method = "GET", string route = "/api/orders/{id}", int? httpStatus = 200, long? durationMicros = 1_000) => new()
    {
        TraceId = traceId,
        Method = method,
        RouteTemplate = route,
        HttpStatus = httpStatus,
        Status = httpStatus is null && durationMicros is null ? FlowStatus.Running : FlowStatus.Success,
        StartedAt = startedAt,
        EndedAt = durationMicros is null ? null : startedAt.AddMicroseconds(durationMicros.Value),
        DurationMicros = durationMicros,
        SpanCount = 1,
        Truncated = false,
    };

    [Fact]
    public async Task QueryAsync_SortsByStartedAt_BothDirections()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeRawTrace("s-1", now.AddMinutes(-10)), [], CancellationToken.None);
        await store.SaveAsync(MakeRawTrace("s-2", now.AddMinutes(-5)), [], CancellationToken.None);
        await store.SaveAsync(MakeRawTrace("s-3", now), [], CancellationToken.None);

        var ascending = await store.QueryAsync(new TraceQuery { SortBy = TraceSortField.StartedAt, SortDirection = SortDirection.Ascending }, CancellationToken.None);
        ascending.Items.Select(i => i.TraceId).ShouldBe(["s-1", "s-2", "s-3"]);

        var descending = await store.QueryAsync(new TraceQuery { SortBy = TraceSortField.StartedAt, SortDirection = SortDirection.Descending }, CancellationToken.None);
        descending.Items.Select(i => i.TraceId).ShouldBe(["s-3", "s-2", "s-1"]);
    }

    [Fact]
    public async Task QueryAsync_SortsByDuration_NullsLastBothDirections()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeRawTrace("d-running", now.AddMinutes(-1), httpStatus: null, durationMicros: null), [], CancellationToken.None);
        await store.SaveAsync(MakeRawTrace("d-slow", now.AddMinutes(-2), durationMicros: 9_000), [], CancellationToken.None);
        await store.SaveAsync(MakeRawTrace("d-fast", now.AddMinutes(-3), durationMicros: 1_000), [], CancellationToken.None);

        var ascending = await store.QueryAsync(new TraceQuery { SortBy = TraceSortField.Duration, SortDirection = SortDirection.Ascending }, CancellationToken.None);
        ascending.Items.Select(i => i.TraceId).ShouldBe(["d-fast", "d-slow", "d-running"]);

        var descending = await store.QueryAsync(new TraceQuery { SortBy = TraceSortField.Duration, SortDirection = SortDirection.Descending }, CancellationToken.None);
        descending.Items.Select(i => i.TraceId).ShouldBe(["d-slow", "d-fast", "d-running"]);
    }

    [Fact]
    public async Task QueryAsync_SortsByHttpStatus_NullsLastBothDirections()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeRawTrace("h-running", now.AddMinutes(-1), httpStatus: null, durationMicros: null), [], CancellationToken.None);
        await store.SaveAsync(MakeRawTrace("h-500", now.AddMinutes(-2), httpStatus: 500), [], CancellationToken.None);
        await store.SaveAsync(MakeRawTrace("h-200", now.AddMinutes(-3), httpStatus: 200), [], CancellationToken.None);

        var ascending = await store.QueryAsync(new TraceQuery { SortBy = TraceSortField.HttpStatus, SortDirection = SortDirection.Ascending }, CancellationToken.None);
        ascending.Items.Select(i => i.TraceId).ShouldBe(["h-200", "h-500", "h-running"]);

        var descending = await store.QueryAsync(new TraceQuery { SortBy = TraceSortField.HttpStatus, SortDirection = SortDirection.Descending }, CancellationToken.None);
        descending.Items.Select(i => i.TraceId).ShouldBe(["h-500", "h-200", "h-running"]);
    }

    [Fact]
    public async Task QueryAsync_SortsByMethod_OrdinalIgnoringCase()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeRawTrace("m-post", now.AddMinutes(-1), method: "post"), [], CancellationToken.None);
        await store.SaveAsync(MakeRawTrace("m-get", now.AddMinutes(-2), method: "GET"), [], CancellationToken.None);
        await store.SaveAsync(MakeRawTrace("m-delete", now.AddMinutes(-3), method: "Delete"), [], CancellationToken.None);

        var ascending = await store.QueryAsync(new TraceQuery { SortBy = TraceSortField.Method, SortDirection = SortDirection.Ascending }, CancellationToken.None);
        ascending.Items.Select(i => i.TraceId).ShouldBe(["m-delete", "m-get", "m-post"]);
    }

    [Fact]
    public async Task QueryAsync_SortsByRoute_OrdinalIgnoringCase()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeRawTrace("r-c", now.AddMinutes(-1), route: "/r/C"), [], CancellationToken.None);
        await store.SaveAsync(MakeRawTrace("r-a", now.AddMinutes(-2), route: "/r/A"), [], CancellationToken.None);
        await store.SaveAsync(MakeRawTrace("r-b", now.AddMinutes(-3), route: "/r/b"), [], CancellationToken.None);

        var ascending = await store.QueryAsync(new TraceQuery { SortBy = TraceSortField.Route, SortDirection = SortDirection.Ascending }, CancellationToken.None);
        ascending.Items.Select(i => i.TraceId).ShouldBe(["r-a", "r-b", "r-c"]);
    }

    [Fact]
    public async Task QueryAsync_SortTieBreak_StartedAtThenTraceId_IsStableForEqualSortValues()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        await store.SaveAsync(MakeRawTrace("tie-b", now.AddMinutes(-1), durationMicros: 5_000), [], CancellationToken.None);
        await store.SaveAsync(MakeRawTrace("tie-a", now.AddMinutes(-1), durationMicros: 5_000), [], CancellationToken.None);
        await store.SaveAsync(MakeRawTrace("tie-older", now.AddMinutes(-5), durationMicros: 5_000), [], CancellationToken.None);

        var result = await store.QueryAsync(new TraceQuery { SortBy = TraceSortField.Duration, SortDirection = SortDirection.Descending }, CancellationToken.None);

        // Same duration everywhere: ties break by StartedAt desc, then TraceId.
        result.Items.Select(i => i.TraceId).ShouldBe(["tie-a", "tie-b", "tie-older"]);
    }

    [Fact]
    public async Task QueryAsync_SortCombinedWithFilterAndPagination_PreservesOrderAcrossPages()
    {
        var store = CreateStore();
        var now = DateTime.UtcNow;
        var durations = new long[] { 1_000, 9_000, 3_000, 7_000, 5_000 };
        for (var i = 0; i < durations.Length; i++)
        {
            await store.SaveAsync(MakeRawTrace($"p-{i}", now.AddMinutes(-i), httpStatus: 200, durationMicros: durations[i]), [], CancellationToken.None);
        }

        var query = new TraceQuery
        {
            Status = FlowStatus.Success,
            SortBy = TraceSortField.Duration,
            SortDirection = SortDirection.Descending,
            PageSize = 2,
        };

        var firstPage = await store.QueryAsync(query with { Page = 1 }, CancellationToken.None);
        var secondPage = await store.QueryAsync(query with { Page = 2 }, CancellationToken.None);
        var thirdPage = await store.QueryAsync(query with { Page = 3 }, CancellationToken.None);

        firstPage.TotalCount.ShouldBe(5);
        var allIds = firstPage.Items.Concat(secondPage.Items).Concat(thirdPage.Items).Select(i => i.TraceId).ToList();
        allIds.ShouldBe(["p-1", "p-3", "p-4", "p-2", "p-0"]);
        allIds.Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public async Task ApplyRetentionAsync_MaxTracesZero_RemovesEverything()
    {
        var store = CreateStore();
        await store.SaveAsync(MakeTrace("clear-1"), [MakeSpan("clear-1", "s1", null, 0)], CancellationToken.None);
        await store.SaveAsync(MakeTrace("clear-2"), [MakeSpan("clear-2", "s1", null, 0)], CancellationToken.None);

        var removed = await store.ApplyRetentionAsync(new RetentionPolicy { MaxTraces = 0, MaxAge = TimeSpan.Zero }, CancellationToken.None);

        removed.ShouldBe(2);
        (await store.GetAsync("clear-1", CancellationToken.None)).ShouldBeNull();
        (await store.GetAsync("clear-2", CancellationToken.None)).ShouldBeNull();
    }
}

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

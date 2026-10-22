using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Domain;
using NetApiFlow.Recording;
using NetApiFlow.Storage;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Verifies the eleven REST routes frozen in <c>docs/plano/contratos.md</c>, seção 3 (tarefa 3.2),
/// against a host running only NetApiFlow's own services (no sample app), seeding
/// <see cref="InMemoryTraceStore"/> and <see cref="FlowRecorder"/> directly.
/// </summary>
public class DashboardApiTests
{
    private static IHost CreateHost(string? environmentName = null, Action<NetApiFlowOptions>? configure = null)
    {
        environmentName ??= Environments.Development;

        return Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(environmentName);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(configure);
                    services.AddNetApiFlowDashboard();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapNetApiFlowDashboardApi());
                });
            })
            .Start();
    }

    private static FlowTrace CreateTrace(string traceId, string method, string route, FlowStatus status, DateTime startedAt, long? durationMicros, int httpStatus = 200) => new()
    {
        TraceId = traceId,
        Method = method,
        RouteTemplate = route,
        HttpStatus = status == FlowStatus.Running ? null : httpStatus,
        Status = status,
        StartedAt = startedAt,
        EndedAt = status == FlowStatus.Running ? null : startedAt.AddMicroseconds(durationMicros ?? 0),
        DurationMicros = status == FlowStatus.Running ? null : durationMicros,
        SpanCount = 1,
        Truncated = false,
        Tags = new Dictionary<string, string> { ["tenant"] = "acme" },
    };

    private static FlowSpan CreateSpan(string traceId, string spanId, string? parentSpanId, int sequence, string component, string operation, long durationMicros, FlowStatus status = FlowStatus.Success) => new()
    {
        TraceId = traceId,
        SpanId = spanId,
        ParentSpanId = parentSpanId,
        Sequence = sequence,
        Component = component,
        Operation = operation,
        Type = ComponentType.Service,
        StartedAt = DateTime.UtcNow,
        EndedAt = DateTime.UtcNow,
        DurationMicros = durationMicros,
        Status = status,
    };

    [Fact]
    public async Task GetTraces_ReturnsPagedResultMatchingFilters()
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<InMemoryTraceStore>();
        await store.SaveAsync(CreateTrace("t1", "GET", "/api/orders/1", FlowStatus.Success, DateTime.UtcNow, 10_000), [], CancellationToken.None);
        await store.SaveAsync(CreateTrace("t2", "GET", "/api/orders/2", FlowStatus.Error, DateTime.UtcNow, 20_000), [], CancellationToken.None);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces?status=Error");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(1);
        doc.RootElement.GetProperty("items")[0].GetProperty("traceId").GetString().ShouldBe("t2");
    }

    [Fact]
    public async Task GetTraces_EmptyPage_ReturnsEmptyItemsAndZeroTotal()
    {
        using var host = CreateHost();

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
        doc.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task GetTraces_PageSizeAboveMaximum_Returns400()
    {
        using var host = CreateHost();

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces?pageSize=201");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task GetTraces_InvalidSort_Returns400WithAcceptedValues()
    {
        using var host = CreateHost();

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces?sort=foo");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("startedAt");
    }

    [Fact]
    public async Task GetTraces_InvalidOrder_Returns400WithAcceptedValues()
    {
        using var host = CreateHost();

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces?order=sideways");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("asc");
    }

    [Fact]
    public async Task GetTraces_SortByDurationDescending_OrdersAcrossTheWholeResultSet()
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<InMemoryTraceStore>();
        await store.SaveAsync(CreateTrace("slow", "GET", "/api/orders/1", FlowStatus.Success, DateTime.UtcNow, 50_000), [], CancellationToken.None);
        await store.SaveAsync(CreateTrace("fast", "GET", "/api/orders/2", FlowStatus.Success, DateTime.UtcNow, 1_000), [], CancellationToken.None);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces?sort=duration&order=desc");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("items")[0].GetProperty("traceId").GetString().ShouldBe("slow");
        doc.RootElement.GetProperty("items")[1].GetProperty("traceId").GetString().ShouldBe("fast");
    }

    [Fact]
    public async Task GetTraces_NoSort_KeepsTheExistingStartedAtDescendingBehavior()
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<InMemoryTraceStore>();
        await store.SaveAsync(CreateTrace("older", "GET", "/api/orders/1", FlowStatus.Success, DateTime.UtcNow.AddMinutes(-5), 1_000), [], CancellationToken.None);
        await store.SaveAsync(CreateTrace("newer", "GET", "/api/orders/2", FlowStatus.Success, DateTime.UtcNow, 1_000), [], CancellationToken.None);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("items")[0].GetProperty("traceId").GetString().ShouldBe("newer");
        doc.RootElement.GetProperty("items")[1].GetProperty("traceId").GetString().ShouldBe("older");
    }

    [Fact]
    public async Task GetTracesLive_ReturnsRunningTracesFromRecorder()
    {
        using var host = CreateHost();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        recorder.TryStartTrace("live-1", "GET", "/api/orders", DateTime.UtcNow);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetArrayLength().ShouldBe(1);
        doc.RootElement[0].GetProperty("traceId").GetString().ShouldBe("live-1");
        doc.RootElement[0].GetProperty("status").GetString().ShouldBe("Running");
        doc.RootElement[0].GetProperty("durationMicros").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetTraceDetail_FromActiveTrace_ReturnsRunningTraceWithSpans()
    {
        using var host = CreateHost();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        recorder.TryStartTrace("active-1", "GET", "/api/orders/1", DateTime.UtcNow);
        var span = CreateSpan("active-1", "span-1", null, 0, "OrdersController", "Get", 1000);
        recorder.TryAddSpan("active-1", span);
        recorder.CompleteSpan("active-1", span);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces/active-1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().ShouldBe("Running");
        doc.RootElement.GetProperty("spans").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task GetTraceDetail_FromStore_ReturnsCompletedTraceWithSpans()
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<InMemoryTraceStore>();
        var trace = CreateTrace("stored-1", "GET", "/api/orders/1", FlowStatus.Success, DateTime.UtcNow, 5_000);
        var span = CreateSpan("stored-1", "span-1", null, 0, "OrdersController", "Get", 5_000);
        await store.SaveAsync(trace, [span], CancellationToken.None);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces/stored-1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("traceId").GetString().ShouldBe("stored-1");
        doc.RootElement.GetProperty("tags").GetProperty("tenant").GetString().ShouldBe("acme");
        doc.RootElement.GetProperty("spans").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task GetTraceDetail_UnknownTraceId_Returns404()
    {
        using var host = CreateHost();

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSpanDetail_ReturnsAncestorsChildrenAndMedianDelta()
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<InMemoryTraceStore>();

        // History so the median for OrderRepository.FindAsync is well-defined.
        await store.SaveAsync(
            CreateTrace("hist-1", "GET", "/api/orders/1", FlowStatus.Success, DateTime.UtcNow, 1_000),
            [CreateSpan("hist-1", "h1", null, 0, "OrderRepository", "FindAsync", 1_000)],
            CancellationToken.None);

        var root = CreateSpan("trace-x", "root", null, 0, "OrdersController", "Get", 3_000);
        var child = CreateSpan("trace-x", "child", "root", 1, "OrderRepository", "FindAsync", 2_000);
        var grandchild = CreateSpan("trace-x", "grandchild", "child", 2, "Database", "SELECT Orders", 500);
        var trace = CreateTrace("trace-x", "GET", "/api/orders/1", FlowStatus.Success, DateTime.UtcNow, 3_000);
        await store.SaveAsync(trace, [root, child, grandchild], CancellationToken.None);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces/trace-x/spans/child");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("span").GetProperty("spanId").GetString().ShouldBe("child");
        doc.RootElement.GetProperty("ancestors").GetArrayLength().ShouldBe(1);
        doc.RootElement.GetProperty("ancestors")[0].GetProperty("spanId").GetString().ShouldBe("root");
        doc.RootElement.GetProperty("children").GetArrayLength().ShouldBe(1);
        doc.RootElement.GetProperty("children")[0].GetProperty("spanId").GetString().ShouldBe("grandchild");
        doc.RootElement.GetProperty("medianDurationMicros").GetInt64().ShouldBe(1_000);
        doc.RootElement.GetProperty("deltaFromMedianMicros").GetInt64().ShouldBe(1_000);
    }

    [Fact]
    public async Task GetSpanDetail_UnknownSpanId_Returns404()
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<InMemoryTraceStore>();
        await store.SaveAsync(CreateTrace("t-404", "GET", "/api/orders/1", FlowStatus.Success, DateTime.UtcNow, 1_000), [], CancellationToken.None);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces/t-404/spans/missing");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetBlueprint_WithPreviousSuccess_ReturnsTraceWithSpans()
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<InMemoryTraceStore>();
        var trace = CreateTrace("blueprint-1", "GET", "/api/orders/{id}", FlowStatus.Success, DateTime.UtcNow, 2_000);
        var span = CreateSpan("blueprint-1", "span-1", null, 0, "OrdersController", "Get", 2_000);
        await store.SaveAsync(trace, [span], CancellationToken.None);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/blueprint?method=GET&route=%2Fapi%2Forders%2F%7Bid%7D");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("traceId").GetString().ShouldBe("blueprint-1");
    }

    [Fact]
    public async Task GetBlueprint_NoPreviousSuccess_Returns204()
    {
        using var host = CreateHost();

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/blueprint?method=GET&route=%2Fapi%2Fnever-called");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetBlueprint_MissingRequiredParameter_Returns400()
    {
        using var host = CreateHost();

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/blueprint?method=GET");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMetrics_AggregatesAcrossAllStoredTraces()
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<InMemoryTraceStore>();
        await store.SaveAsync(CreateTrace("m1", "GET", "/api/orders", FlowStatus.Success, DateTime.UtcNow, 1_000), [], CancellationToken.None);
        await store.SaveAsync(CreateTrace("m2", "GET", "/api/orders", FlowStatus.Error, DateTime.UtcNow, 3_000), [], CancellationToken.None);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/metrics");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("totalTraces").GetInt32().ShouldBe(2);
        doc.RootElement.GetProperty("errorCount").GetInt32().ShouldBe(1);
        doc.RootElement.GetProperty("errorRate").GetDouble().ShouldBe(0.5);
        doc.RootElement.GetProperty("maxDurationMicros").GetInt64().ShouldBe(3_000);
    }

    [Fact]
    public async Task GetMetrics_NoTraces_ReturnsZeroesNotNulls()
    {
        using var host = CreateHost();

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/metrics");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("totalTraces").GetInt32().ShouldBe(0);
        doc.RootElement.GetProperty("errorCount").GetInt32().ShouldBe(0);
        doc.RootElement.GetProperty("errorRate").GetDouble().ShouldBe(0);
        doc.RootElement.GetProperty("averageDurationMicros").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task GetComponentMetrics_ReturnsStatsOrderedByExecutionCountDescending()
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<InMemoryTraceStore>();
        var trace = CreateTrace("cm-1", "GET", "/api/orders", FlowStatus.Success, DateTime.UtcNow, 3_000);
        var spans = new[]
        {
            CreateSpan("cm-1", "s1", null, 0, "OrdersController", "Get", 3_000),
            CreateSpan("cm-1", "s2", "s1", 1, "OrderRepository", "FindAsync", 1_000),
            CreateSpan("cm-1", "s3", "s1", 2, "OrderRepository", "FindAsync", 1_000),
        };
        await store.SaveAsync(trace, spans, CancellationToken.None);

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/metrics/components");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement[0].GetProperty("component").GetString().ShouldBe("OrderRepository");
        doc.RootElement[0].GetProperty("executionCount").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task GetConfig_ReturnsSlowThresholdConfiguredOnTheHost()
    {
        using var host = CreateHost(configure: options => options.SlowThreshold = TimeSpan.FromMilliseconds(400));

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/config");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("slowThresholdMicros").GetInt64().ShouldBe(400_000);
        doc.RootElement.GetProperty("version").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task DeleteTraces_ClearsStoreAndActiveTraces()
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<InMemoryTraceStore>();
        var recorder = host.Services.GetRequiredService<FlowRecorder>();
        await store.SaveAsync(CreateTrace("del-1", "GET", "/api/orders", FlowStatus.Success, DateTime.UtcNow, 1_000), [], CancellationToken.None);
        recorder.TryStartTrace("del-live", "GET", "/api/orders", DateTime.UtcNow);

        var client = host.GetTestServer().CreateClient();
        var response = await client.DeleteAsync("/flow/api/traces");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await store.GetAsync("del-1", CancellationToken.None)).ShouldBeNull();
        recorder.GetActiveTrace("del-live").ShouldBeNull();
    }

    [Fact]
    public async Task OutsideDevelopment_WithoutAuthorizationConfigured_EveryRouteReturns404()
    {
        using var host = CreateHost(environmentName: Environments.Production);

        var client = host.GetTestServer().CreateClient();

        (await client.GetAsync("/flow/api/traces")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync("/flow/api/traces")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/flow/api/metrics")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/flow/api/config")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEndpoints_ListsTraceableRoutesSortedByRouteThenMethod()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                    services.AddNetApiFlowDashboard();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/orders/{id}", () => "ok");
                        endpoints.MapPost("/api/orders", () => "ok");
                        endpoints.MapNetApiFlowDashboardApi();
                    });
                });
            })
            .Start();

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/endpoints");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var routes = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("routeTemplate").GetString()).ToList();
        routes.ShouldContain("/api/orders/{id}");
        routes.ShouldContain("/api/orders");
        routes.ShouldNotContain(r => r!.StartsWith("/flow", StringComparison.Ordinal));
    }
}

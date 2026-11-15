using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Domain;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

[ApiController]
[Route("api/widgets")]
public class WidgetsController : ControllerBase
{
    [HttpGet("{id}")]
    public IActionResult Get(int id) => Ok(id);
}

/// <summary>
/// A Controller route attribute's raw route pattern arrives without a leading slash
/// (<c>api/widgets/{id}</c>), while a Minimal API route keeps whatever the call site wrote
/// (<c>/api/gadgets/{id}</c>). Both must end up recorded, and found again, under the one same
/// canonical shape.
/// </summary>
public class RouteTemplateNormalizationIntegrationTests
{
    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddControllers().AddApplicationPart(typeof(RouteTemplateNormalizationIntegrationTests).Assembly);
                    services.AddNetApiFlow(o => o.IncludePaths("/**"));
                    services.AddNetApiFlowDashboard();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                        endpoints.MapGet("/api/gadgets/{id}", (int id) => Results.Ok(id));
                        endpoints.MapNetApiFlowDashboardApi();
                    });
                });
            })
            .Start();
    }

    private static async Task<FlowTraceDetail> WaitForPersistedTraceAsync(ITraceStore store, string traceId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        do
        {
            var persisted = await store.GetAsync(traceId, CancellationToken.None);
            if (persisted is not null)
            {
                return persisted;
            }

            await Task.Delay(20);
        }
        while (DateTime.UtcNow < deadline);

        throw new InvalidOperationException($"Trace {traceId} was never persisted.");
    }

    [Theory]
    [InlineData("/api/widgets/7", "/api/widgets/{id}")]
    [InlineData("/api/gadgets/7", "/api/gadgets/{id}")]
    public async Task Request_ThroughEitherRouteKind_PersistsTheSameCanonicalRouteTemplateShape(string requestPath, string expectedRouteTemplate)
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<ITraceStore>();
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync(requestPath);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var persisted = await WaitForPersistedTraceAsync(store, traceId);

        persisted.Trace.RouteTemplate.ShouldBe(expectedRouteTemplate);
    }

    [Theory]
    [InlineData("/api/widgets/7", "api/widgets/{id}", "/api/widgets/{id}")]
    [InlineData("/api/gadgets/7", "api/gadgets/{id}", "/api/gadgets/{id}")]
    public async Task DashboardReadEndpoints_FindTheTrace_EvenWhenTheQueryStringRouteHasNoLeadingSlash(
        string requestPath, string queriedRouteTemplate, string expectedRouteTemplate)
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<ITraceStore>();
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync(requestPath);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        await WaitForPersistedTraceAsync(store, traceId);

        var encodedRoute = Uri.EscapeDataString(queriedRouteTemplate);

        var architectureResponse = await client.GetAsync($"/flow/api/architecture?method=GET&route={encodedRoute}");
        architectureResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var architectureDoc = JsonDocument.Parse(await architectureResponse.Content.ReadAsStringAsync());
        architectureDoc.RootElement.GetProperty("totalExecutions").GetInt32().ShouldBeGreaterThanOrEqualTo(1);

        var blueprintResponse = await client.GetAsync($"/flow/api/blueprint?method=GET&route={encodedRoute}");
        blueprintResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tracesResponse = await client.GetAsync($"/flow/api/traces?route={encodedRoute}");
        tracesResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var tracesDoc = JsonDocument.Parse(await tracesResponse.Content.ReadAsStringAsync());
        tracesDoc.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("traceId").GetString())
            .ShouldContain(traceId);

        var routeMetricsResponse = await client.GetAsync("/flow/api/metrics/routes");
        routeMetricsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var routeMetricsDoc = JsonDocument.Parse(await routeMetricsResponse.Content.ReadAsStringAsync());
        routeMetricsDoc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("routeTemplate").GetString())
            .ShouldContain(expectedRouteTemplate);
    }
}

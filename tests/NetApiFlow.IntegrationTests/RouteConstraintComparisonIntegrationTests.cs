using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Domain;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// An OpenAPI document never carries ASP.NET routing constraints: the Runner calls a route
/// typed <c>{id:int}</c> through its OpenAPI-documented shape, <c>{id}</c>. Blueprint and
/// architecture, both read with that constraint-free shape, must still find the trace recorded
/// under the constrained one.
/// </summary>
public class RouteConstraintComparisonIntegrationTests
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
                    services.AddNetApiFlow(o => o.IncludePaths("/**"));
                    services.AddNetApiFlowDashboard();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/widgets/{id:int}", (int id) => Results.Ok(id));
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
    [InlineData("/api/widgets/{id}")]
    [InlineData("/api/widgets/{id:int}")]
    public async Task BlueprintAndArchitecture_QueriedWithOrWithoutTheConstraint_FindTheTraceRecordedWithTheConstraint(string queriedRoute)
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<ITraceStore>();
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync("/api/widgets/7");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();
        var persisted = await WaitForPersistedTraceAsync(store, traceId);
        persisted.Trace.RouteTemplate.ShouldBe("/api/widgets/{id:int}");

        var encodedRoute = Uri.EscapeDataString(queriedRoute);

        var architectureResponse = await client.GetAsync($"/flow/api/architecture?method=GET&route={encodedRoute}");
        architectureResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var architectureDoc = JsonDocument.Parse(await architectureResponse.Content.ReadAsStringAsync());
        architectureDoc.RootElement.GetProperty("totalExecutions").GetInt32().ShouldBeGreaterThanOrEqualTo(1);

        var blueprintResponse = await client.GetAsync($"/flow/api/blueprint?method=GET&route={encodedRoute}");
        blueprintResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var blueprintDoc = JsonDocument.Parse(await blueprintResponse.Content.ReadAsStringAsync());
        blueprintDoc.RootElement.GetProperty("traceId").GetString().ShouldBe(traceId);
    }
}

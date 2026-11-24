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
using NetApiFlow.Storage;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class BlueprintAnchorTests
{
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";
    private const string OrdersRoute = "%2Forders%2F%7Bid%3Aint%7D";

    private static IHost StartHost() =>
        Host.CreateDefaultBuilder()
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
                        endpoints.MapNetApiFlowDashboardApi();
                        endpoints.MapGet("/orders/{id:int}", async (int id, bool cached) =>
                        {
                            using (Flow.Step("OrderCache", "TryGet"))
                            {
                                await Task.Delay(5);
                            }

                            if (cached)
                            {
                                return "cached";
                            }

                            using (Flow.Step("OrderRepository", "Find"))
                            {
                                await Task.Delay(5);
                            }

                            return "loaded";
                        }).WithNetApiFlowTracking();
                    });
                });
            })
            .Start();

    private static async Task<(string TraceId, string StartedAt)> RunOrderAsync(IHost host, HttpClient client, int id, bool cached)
    {
        var response = await client.GetAsync($"/orders/{id}?cached={cached.ToString().ToLowerInvariant()}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues(TraceIdHeader).Single();
        var persisted = await host.Services.GetRequiredService<InMemoryTraceStore>().WaitForPersistedTraceAsync(traceId);
        return (traceId, persisted.Trace.StartedAt.ToString("O"));
    }

    private static async Task<JsonDocument?> BlueprintAsync(HttpClient client, string startedAt)
    {
        var response = await client.GetAsync($"/flow/api/blueprint?method=GET&route={OrdersRoute}&before={Uri.EscapeDataString(startedAt)}");
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Blueprint_OfAnEarlyReturnTrace_NeverComesFromALaterExecution()
    {
        using var host = StartHost();
        var client = host.GetTestServer().CreateClient();
        var a = await RunOrderAsync(host, client, 1, cached: true);
        var b = await RunOrderAsync(host, client, 2, cached: false);
        var c = await RunOrderAsync(host, client, 3, cached: true);

        using var blueprintOfA = await BlueprintAsync(client, a.StartedAt);
        using var blueprintOfB = await BlueprintAsync(client, b.StartedAt);
        using var blueprintOfC = await BlueprintAsync(client, c.StartedAt);

        blueprintOfA.ShouldBeNull();
        blueprintOfB.ShouldNotBeNull().RootElement.GetProperty("traceId").GetString().ShouldBe(a.TraceId);
        blueprintOfC.ShouldNotBeNull().RootElement.GetProperty("traceId").GetString().ShouldBe(b.TraceId);
        var stepsOfTheBlueprintOfC = blueprintOfC.RootElement.GetProperty("spans").EnumerateArray().Select(span => span.GetProperty("component").GetString()).ToList();
        stepsOfTheBlueprintOfC.ShouldContain("OrderRepository");
    }
}

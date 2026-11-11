using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class GetRecentTraceIntegrationTests
{
    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(o => o.IncludePaths("/**"));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/orders/{id}", async () =>
                        {
                            using var controllerStep = NetApiFlow.Step("OrdersController", "GetOrder", ComponentType.Controller);
                            using var serviceStep = NetApiFlow.Step("OrderService", "GetAsync", ComponentType.Service);
                            using (NetApiFlow.Step("OrderRepository", "FindAsync", ComponentType.Database))
                            {
                                await Task.Delay(1);
                            }

                            return Results.Ok();
                        });
                    });
                });
            })
            .Start();
    }

    [Fact]
    public async Task GetRecentTrace_RightAfterOwnHttpCall_ReturnsTraceWithAllSpansClosed()
    {
        using var host = CreateHost();
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync("/api/orders/42");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();

        var trace = NetApiFlow.GetRecentTrace(host.Services, traceId);

        trace.ShouldNotBeNull();
        trace.Trace.TraceId.ShouldBe(traceId);
        trace.Spans.Count.ShouldBe(3);
        trace.Spans.ShouldAllBe(s => s.EndedAt != null);
        trace.Spans.Count(s => s.Type == ComponentType.Database).ShouldBe(1);
    }

    [Fact]
    public async Task GetRecentTrace_WithTraceIdThatNeverExisted_ReturnsNull()
    {
        using var host = CreateHost();

        var trace = NetApiFlow.GetRecentTrace(host.Services, "4bf92f3577b34da6a3ce929d0e0e4736");

        trace.ShouldBeNull();
    }

    [Fact]
    public void GetRecentTrace_WhenNetApiFlowWasNeverRegistered_ReturnsNull()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        var trace = NetApiFlow.GetRecentTrace(services, "4bf92f3577b34da6a3ce929d0e0e4736");

        trace.ShouldBeNull();
    }
}

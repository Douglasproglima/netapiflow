using System.Net;
using System.Text.Json;
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

public class UnmatchedRouteTracingIntegrationTests
{
    private const string DeviceIdentifier = "359872104738291";

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
                    app.UseEndpoints(endpoints => endpoints.MapGet("/api/orders/{id}", () => Results.Ok()));
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

    [Fact]
    public async Task Request_WithoutMatchingRoute_NeverPersistsTheRequestedPath()
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<ITraceStore>();
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync($"/api/devices/{DeviceIdentifier}/status");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();

        var persisted = await WaitForPersistedTraceAsync(store, traceId);

        persisted.Trace.RouteTemplate.ShouldNotContain(DeviceIdentifier);
        JsonSerializer.Serialize(persisted).ShouldNotContain(DeviceIdentifier);
    }

    [Fact]
    public async Task Request_WithMatchingRoute_StillPersistsTheRouteTemplate()
    {
        using var host = CreateHost();
        var store = host.Services.GetRequiredService<ITraceStore>();
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync("/api/orders/42");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var traceId = response.Headers.GetValues("X-NetApiFlow-TraceId").Single();

        var persisted = await WaitForPersistedTraceAsync(store, traceId);

        persisted.Trace.RouteTemplate.ShouldBe("/api/orders/{id}");
    }
}

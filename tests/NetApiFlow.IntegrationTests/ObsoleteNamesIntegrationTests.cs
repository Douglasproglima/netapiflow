#pragma warning disable CS0618

using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Domain;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class ObsoleteNamesIntegrationTests
{
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";

    [Fact]
    public void NetApiFlow_IsObsoleteAndPointsToFlow()
    {
        var obsolete = typeof(NetApiFlow).GetCustomAttribute<ObsoleteAttribute>().ShouldNotBeNull();

        obsolete.Message.ShouldBe("Use NetApiFlow.Flow.");
        obsolete.IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task NetApiFlowStep_RecordsTheSameStepAsFlowStep()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestServer().CreateClient();

        var oldName = await client.GetAsync("/old-name");
        var newName = await client.GetAsync("/new-name");

        var oldStep = StepOf(host, oldName);
        var newStep = StepOf(host, newName);
        (oldStep.Component, oldStep.Operation, oldStep.Type).ShouldBe(("OrderService", "PlaceAsync", ComponentType.Service));
        (newStep.Component, newStep.Operation, newStep.Type).ShouldBe((oldStep.Component, oldStep.Operation, oldStep.Type));
        (await oldName.Content.ReadAsStringAsync()).ShouldBe(TraceIdOf(oldName));
    }

    private static FlowSpan StepOf(IHost host, HttpResponseMessage response) =>
        NetApiFlow.GetRecentTrace(host.Services, TraceIdOf(response)).ShouldNotBeNull()
            .Spans.Single(span => span.Type == ComponentType.Service);

    private static string TraceIdOf(HttpResponseMessage response) => response.Headers.GetValues(TraceIdHeader).Single();

    private static async Task<IHost> StartHostAsync()
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/old-name", () =>
                        {
                            using (NetApiFlow.Step("OrderService", "PlaceAsync", ComponentType.Service))
                            {
                                return NetApiFlow.CurrentTraceId;
                            }
                        });
                        endpoints.MapGet("/new-name", () =>
                        {
                            using (Flow.Step("OrderService", "PlaceAsync", ComponentType.Service))
                            {
                                return Flow.CurrentTraceId;
                            }
                        });
                    });
                });
            })
            .StartAsync();
    }
}

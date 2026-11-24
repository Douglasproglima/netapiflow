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

public class EndpointTrackingConventionTests
{
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";

    [Theory]
    [InlineData("/through-the-interface")]
    [InlineData("/route-handler")]
    [InlineData("/group/inside")]
    public async Task WithNetApiFlowTracking_OnAnyConventionBuilder_RecordsOneEndpointStep(string path)
    {
        using var host = await StartHostAsync();

        var response = await host.GetTestServer().CreateClient().GetAsync(path);

        var trace = Flow.GetRecentTrace(host.Services, response.Headers.GetValues(TraceIdHeader).Single()).ShouldNotBeNull();
        trace.Spans.Count(span => span.Type == ComponentType.Endpoint).ShouldBe(1);
    }

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
                    services.AddNetApiFlow(options => options.CaptureMinimalApiEndpoints = false);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        IEndpointConventionBuilder throughTheInterface = endpoints.MapGet("/through-the-interface", () => "ok");
                        throughTheInterface.WithNetApiFlowTracking();
                        endpoints.MapGet("/route-handler", () => "ok").WithNetApiFlowTracking();
                        endpoints.MapGroup("/group").WithNetApiFlowTracking().MapGet("/inside", () => "ok");
                    });
                });
            })
            .StartAsync();
    }
}

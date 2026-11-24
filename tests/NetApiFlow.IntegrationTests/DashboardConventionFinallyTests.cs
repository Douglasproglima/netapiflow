using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class DashboardConventionFinallyTests
{
    [Fact]
    public async Task MapNetApiFlowDashboard_WithAFinallyConvention_AppliesItToEveryDashboardRoute()
    {
        using var host = await Host.CreateDefaultBuilder()
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
                    app.UseEndpoints(endpoints => endpoints.MapNetApiFlowDashboard()
                        .Finally(endpoint => endpoint.Metadata.Add(new AddedLast())));
                });
            })
            .StartAsync();

        var dashboardRoutes = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/flow", StringComparison.Ordinal) == true)
            .ToList();

        dashboardRoutes.ShouldNotBeEmpty();
        dashboardRoutes.ShouldContain(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/flow/api", StringComparison.Ordinal));
        dashboardRoutes.ShouldAllBe(endpoint => endpoint.Metadata.GetMetadata<AddedLast>() != null);
    }

    private sealed class AddedLast;
}

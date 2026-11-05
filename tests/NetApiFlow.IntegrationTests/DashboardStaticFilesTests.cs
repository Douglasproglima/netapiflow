using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

/// <summary>
/// Verifies the embedded Angular UI is served and its &lt;base href&gt; rewritten for the host's
/// actual mount path (risco R-03).
/// </summary>
public class DashboardStaticFilesTests
{
    private static IHost CreateHost(string dashboardPath, string? pathBase = null)
    {
        return Host.CreateDefaultBuilder()
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
                    if (pathBase is not null)
                    {
                        app.UsePathBase(pathBase);
                    }

                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.UseNetApiFlowDashboard(dashboardPath));
                });
            })
            .Start();
    }

    [Fact]
    public async Task GetDashboardRoot_RewritesBaseHrefWithPathBaseAndDashboardPath()
    {
        using var host = CreateHost("/qualquer/caminho", pathBase: "/app");

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/app/qualquer/caminho");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("<base href=\"/app/qualquer/caminho/\">");
    }

    [Fact]
    public async Task GetDashboardRoot_WithoutPathBase_RewritesBaseHrefWithDashboardPathOnly()
    {
        using var host = CreateHost("/flow");

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("<base href=\"/flow/\">");
    }

    [Fact]
    public async Task GetStaticAsset_ServesTheEmbeddedFileWithItsContentType()
    {
        using var host = CreateHost("/flow");

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/favicon.ico");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUnknownRouteUnderDashboardPath_FallsBackToIndexHtml()
    {
        using var host = CreateHost("/flow");

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/history/some-deep-link");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("<app-root>");
    }

    [Fact]
    public async Task GetApiRoute_IsNotShadowedByTheStaticCatchAll()
    {
        using var host = CreateHost("/flow");

        var client = host.GetTestServer().CreateClient();
        var response = await client.GetAsync("/flow/api/traces");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }
}

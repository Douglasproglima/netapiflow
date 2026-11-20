using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Dashboard;
using NetApiFlow.Dashboard.Endpoints;
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class OpenApiInternalRoutesTests
{
    private const string UserRoute = "/api/orders";
    private const string DefaultDashboardBasePath = "/flow";
    private const string DefaultMcpBasePath = "/flow/mcp";

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task DocumentedPaths_WithDashboardAndMcpMounted_ListOnlyTheUserRoutes(string environmentName)
    {
        using var host = await StartHostAsync(environmentName);

        var paths = await ListDocumentedPathsAsync(host);

        paths.ShouldContain(UserRoute);
        paths.Where(path => IsUnder(path, DefaultDashboardBasePath)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DocumentedPaths_WithDashboardAndMcpAtCustomPaths_ListNothingUnderThem()
    {
        using var host = await StartHostAsync("Production", "/internal/flow", "/internal/mcp");

        var paths = await ListDocumentedPathsAsync(host);

        paths.ShouldContain(UserRoute);
        paths.Where(path => IsUnder(path, "/internal/flow") || IsUnder(path, "/internal/mcp")).ShouldBeEmpty();
    }

    [Fact]
    public async Task McpEndpoints_WhenMounted_AreMarkedAsExcludedFromDescription()
    {
        using var host = await StartHostAsync("Development");

        var mcpEndpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => IsUnder("/" + endpoint.RoutePattern.RawText!.Trim('/'), DefaultMcpBasePath))
            .ToList();

        mcpEndpoints.ShouldNotBeEmpty();
        mcpEndpoints.ShouldAllBe(endpoint => IsExcludedFromDescription(endpoint));
    }

    [Fact]
    public async Task GetEndpoints_WithDashboardAndMcpMounted_ListsTheUserRoute()
    {
        using var host = await StartHostAsync("Development");
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync($"{DefaultDashboardBasePath}/api/endpoints");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var listed = body.RootElement.EnumerateArray()
            .Select(entry => $"{entry.GetProperty("method").GetString()} {entry.GetProperty("routeTemplate").GetString()}")
            .Where(entry => !entry.Contains("/openapi/", StringComparison.Ordinal))
            .ToList();
        listed.ShouldBe([$"GET {UserRoute}"]);
    }

    [Theory]
    [InlineData("GET", "/flow")]
    [InlineData("GET", "/flow/api/traces")]
    [InlineData("DELETE", "/flow/api/traces")]
    [InlineData("GET", "/flow/api/config")]
    [InlineData("POST", "/flow/api/deploys")]
    [InlineData("POST", "/flow/mcp")]
    public async Task InternalRoutes_InProductionWithoutAuthorization_Return404(string method, string path)
    {
        using var host = await StartHostAsync("Production");
        var client = host.GetTestServer().CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static bool IsExcludedFromDescription(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription == true;

    private static bool IsUnder(string path, string basePath) =>
        path.Equals(basePath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase);

    private static async Task<IHost> StartHostAsync(
        string environmentName,
        string dashboardBasePath = DefaultDashboardBasePath,
        string mcpBasePath = DefaultMcpBasePath)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(environmentName);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    AddOpenApiGenerator(services);
                    services.AddNetApiFlow();
                    services.AddNetApiFlowDashboard();
                    services.AddNetApiFlowMcp();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet(UserRoute, () => "orders");
                        MapOpenApiDocument(endpoints);
                        endpoints.UseNetApiFlowDashboard(dashboardBasePath);
                        endpoints.UseNetApiFlowMcp(mcpBasePath);
                    });
                });
            })
            .StartAsync();
    }

#if NET10_0_OR_GREATER
    private static void AddOpenApiGenerator(IServiceCollection services) => services.AddOpenApi();

    private static void MapOpenApiDocument(IEndpointRouteBuilder endpoints) => endpoints.MapOpenApi();

    private static async Task<IReadOnlyList<string>> ListDocumentedPathsAsync(IHost host)
    {
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToList();
    }
#else
    private static void AddOpenApiGenerator(IServiceCollection services) => services.AddEndpointsApiExplorer();

    private static void MapOpenApiDocument(IEndpointRouteBuilder endpoints)
    {
    }

    private static Task<IReadOnlyList<string>> ListDocumentedPathsAsync(IHost host)
    {
        var provider = host.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>();
        var paths = provider.ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Select(description => "/" + description.RelativePath)
            .Distinct()
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(paths);
    }
#endif
}

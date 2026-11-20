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
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class OpenApiDocumentTracingIntegrationTests
{
    private const string UserRoute = "/api/orders";
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";

    [Theory]
    [InlineData("/openapi/v1.json", "/openapi/v1.json")]
    [InlineData("/openapi/v1.json", "/OPENAPI/V1.JSON")]
    [InlineData("/swagger/v1/swagger.json", "/swagger/v1/swagger.json")]
    public async Task DocumentRequest_AtTheConfiguredPath_IsNeitherTracedNorCounted(string configuredPath, string requestedPath)
    {
        using var host = await StartHostAsync(configuredPath);
        var client = host.GetTestServer().CreateClient();

        var documentResponse = await client.GetAsync(requestedPath);
        var userResponse = await client.GetAsync(UserRoute);
        await host.Services.GetRequiredService<ITraceStore>()
            .WaitForPersistedTraceAsync(userResponse.Headers.GetValues(TraceIdHeader).Single());

        documentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        documentResponse.Headers.Contains(TraceIdHeader).ShouldBeFalse();
        (await ListTracedRoutesAsync(client)).ShouldBe([UserRoute]);
        (await ReadTotalTracesAsync(client)).ShouldBe(1);
    }

    [Fact]
    public async Task DocumentRequest_AtADifferentPathFromTheConfiguredOne_IsStillTraced()
    {
        using var host = await StartHostAsync("/swagger/v1/swagger.json");
        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        await host.Services.GetRequiredService<ITraceStore>()
            .WaitForPersistedTraceAsync(response.Headers.GetValues(TraceIdHeader).Single());

        (await ListTracedRoutesAsync(client)).ShouldBe(["/openapi/v1.json"]);
    }

    private static async Task<IHost> StartHostAsync(string openApiDocumentPath)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(options => options.OpenApiDocumentPath = openApiDocumentPath);
                    services.AddNetApiFlowDashboard();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet(UserRoute, () => Results.Ok());
                        endpoints.MapGet("/openapi/v1.json", () => Results.Text("{}", "application/json"));
                        endpoints.MapGet("/swagger/v1/swagger.json", () => Results.Text("{}", "application/json"));
                        endpoints.UseNetApiFlowDashboard();
                    });
                });
            })
            .StartAsync();
    }

    private static async Task<IReadOnlyList<string>> ListTracedRoutesAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/flow/api/traces"));
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Select(trace => trace.GetProperty("routeTemplate").GetString()!)
            .ToList();
    }

    private static async Task<int> ReadTotalTracesAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/flow/api/metrics"));
        return body.RootElement.GetProperty("totalTraces").GetInt32();
    }
}

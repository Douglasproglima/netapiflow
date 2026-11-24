using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NetApiFlow.Mcp.Endpoints;
using Shouldly;

namespace NetApiFlow.Mcp.Tests;

public class McpServerInfoWithAppServerTests
{
    [Fact]
    public async Task Initialize_OnTheMcpServerOfTheApp_KeepsTheNameTheAppGaveIt()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddMcpServer(options => options.ServerInfo = new Implementation { Name = "MyApp", Version = "1.0.0" })
                        .WithHttpTransport();
                    services.AddNetApiFlow();
                    services.AddNetApiFlowMcp();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapMcp("/mcp");
                        endpoints.MapNetApiFlowMcp();
                    });
                });
            })
            .StartAsync();
        var httpClient = host.GetTestServer().CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/mcp") },
            httpClient,
            ownsHttpClient: false);

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);

        client.ServerInfo.ShouldNotBeNull().Name.ShouldBe("MyApp");
    }
}

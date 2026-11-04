using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;
using Shouldly;
using Xunit;

namespace NetApiFlow.Mcp.Tests;

public class NetApiFlowMcpEndpointTests
{
    [Fact]
    public async Task UseNetApiFlowMcp_InDevelopment_HandshakeSucceeds()
    {
        using var host = await StartHostAsync(Environments.Development);
        var httpClient = host.GetTestServer().CreateClient();

        await using var client = await ConnectAsync(httpClient);

        client.ServerInfo.ShouldNotBeNull();
    }

    private static async Task<McpClient> ConnectAsync(HttpClient httpClient)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/flow/mcp") },
            httpClient,
            ownsHttpClient: false);

        return await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);
    }

    private static async Task<IHost> StartHostAsync(string environmentName, Action<IEndpointConventionBuilder>? configureMcp = null)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(environmentName);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                    services.AddNetApiFlowMcp();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        var mcp = endpoints.UseNetApiFlowMcp();
                        configureMcp?.Invoke(mcp);
                    });
                });
            })
            .StartAsync();
    }
}

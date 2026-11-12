using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;

namespace NetApiFlow.Mcp.Tests;

/// <summary>An MCP server under test, hosted in-process on <see cref="TestServer"/>, with a connected real MCP client.</summary>
internal sealed class McpTestFixture : IAsyncDisposable
{
    private readonly IHost _host;

    private McpTestFixture(IHost host, ITraceStore store, McpClient client)
    {
        _host = host;
        Store = store;
        Client = client;
    }

    public ITraceStore Store { get; }

    public McpClient Client { get; }

    public static async Task<McpTestFixture> StartAsync()
    {
        var host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow();
                    services.AddNetApiFlowMcp();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.UseNetApiFlowMcp());
                });
            })
            .StartAsync();

        var httpClient = host.GetTestServer().CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/flow/mcp") },
            httpClient,
            ownsHttpClient: false);
        var client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);

        var store = host.Services.GetRequiredService<ITraceStore>();
        return new McpTestFixture(host, store, client);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        _host.Dispose();
    }
}

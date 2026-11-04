using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using NetApiFlow.Domain;
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;
using Shouldly;
using Xunit;

namespace NetApiFlow.Mcp.Tests;

public class ListRecentTracesToolTests
{
    [Fact]
    public async Task ListRecentTraces_WithoutFilters_ReturnsAllTraces()
    {
        await using var fixture = await McpTestFixture.StartAsync();
        await fixture.Store.SaveAsync(CreateTrace("trace-1", "GET", FlowStatus.Success), [], CancellationToken.None);
        await fixture.Store.SaveAsync(CreateTrace("trace-2", "POST", FlowStatus.Error), [], CancellationToken.None);

        var result = await fixture.Client.CallToolAsync("list_recent_traces", cancellationToken: CancellationToken.None);

        var traces = ReadTraces(result);
        traces.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ListRecentTraces_FilteredByMethod_ReturnsOnlyMatching()
    {
        await using var fixture = await McpTestFixture.StartAsync();
        await fixture.Store.SaveAsync(CreateTrace("trace-1", "GET", FlowStatus.Success), [], CancellationToken.None);
        await fixture.Store.SaveAsync(CreateTrace("trace-2", "POST", FlowStatus.Error), [], CancellationToken.None);

        var result = await fixture.Client.CallToolAsync(
            "list_recent_traces",
            new Dictionary<string, object?> { ["method"] = "POST" },
            cancellationToken: CancellationToken.None);

        var traces = ReadTraces(result);
        traces.Count.ShouldBe(1);
        traces[0].GetProperty("method").GetString().ShouldBe("POST");
    }

    [Fact]
    public async Task ListRecentTraces_WithInvalidStatus_ReturnsToolErrorWithoutCrashingServer()
    {
        await using var fixture = await McpTestFixture.StartAsync();

        var result = await fixture.Client.CallToolAsync(
            "list_recent_traces",
            new Dictionary<string, object?> { ["status"] = "NotAStatus" },
            cancellationToken: CancellationToken.None);

        result.IsError.ShouldBe(true);

        var followUp = await fixture.Client.CallToolAsync("list_recent_traces", cancellationToken: CancellationToken.None);
        followUp.IsError.ShouldNotBe(true);
    }

    private static List<JsonElement> ReadTraces(ModelContextProtocol.Protocol.CallToolResult result)
    {
        var text = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text;
        return [.. JsonDocument.Parse(text).RootElement.EnumerateArray()];
    }

    private static FlowTrace CreateTrace(string traceId, string method, FlowStatus status) => new()
    {
        TraceId = traceId,
        Method = method,
        RouteTemplate = "/api/orders/{id}",
        Status = status,
        StartedAt = DateTime.UtcNow,
    };

    private sealed class McpTestFixture : IAsyncDisposable
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
}

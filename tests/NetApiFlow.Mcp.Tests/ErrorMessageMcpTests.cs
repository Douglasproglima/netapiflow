using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NetApiFlow.Mcp;
using NetApiFlow.Mcp.Endpoints;
using NetApiFlow.TestSupport;
using Shouldly;
using Xunit;

namespace NetApiFlow.Mcp.Tests;

public class ErrorMessageMcpTests(GetTraceResultsForEveryCase results) : IClassFixture<GetTraceResultsForEveryCase>
{
    [Theory]
    [MemberData(nameof(SecretLeakCases.Ids), MemberType = typeof(SecretLeakCases))]
    public void GetTrace_WithASecretInTheMessage_ReturnsNoFragmentOfItAndNoLongerMessage(int id)
    {
        var payload = results.PayloadOf(id);

        payload.ShouldContain("\"errorMessage\"");
        foreach (var fragment in SecretLeakCases.ById(id).SecretFragments)
        {
            payload.ShouldNotContain(fragment);
        }

        using var document = JsonDocument.Parse(payload);
        foreach (var message in MessageFieldsOf(document.RootElement))
        {
            message.Length.ShouldBeLessThanOrEqualTo(SecretLeakCases.MaxOutputLength);
        }
    }

    private static IEnumerable<string> MessageFieldsOf(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String && IsMessageField(property.Name))
                    {
                        yield return property.Value.GetString() ?? string.Empty;
                    }

                    foreach (var nested in MessageFieldsOf(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in MessageFieldsOf(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private static bool IsMessageField(string name) =>
        name.Equals("errorMessage", StringComparison.OrdinalIgnoreCase)
        || name.Equals("message", StringComparison.OrdinalIgnoreCase)
        || name.Equals("exceptionMessage", StringComparison.OrdinalIgnoreCase);
}

public sealed class GetTraceResultsForEveryCase : IAsyncLifetime, IDisposable
{
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Dictionary<int, string> _payloadsByCase = [];
    private IHost? _host;
    private McpClient? _client;

    public string PayloadOf(int id) => _payloadsByCase[id];

    public async Task InitializeAsync()
    {
        _host = await StartHostAsync();
        var httpClient = _host.GetTestServer().CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/flow/mcp") },
            httpClient,
            ownsHttpClient: false);
        _client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);
        var store = _host.Services.GetRequiredService<ITraceStore>();

        foreach (var leakCase in SecretLeakCases.All)
        {
            var response = await httpClient.GetAsync($"/boom/{leakCase.Id}");
            var traceId = response.Headers.GetValues(TraceIdHeader).Single();
            await WaitUntilPersistedAsync(store, traceId);

            var result = await _client.CallToolAsync(
                "get_trace",
                new Dictionary<string, object?> { ["traceId"] = traceId },
                cancellationToken: CancellationToken.None);
            _payloadsByCase[leakCase.Id] = result.Content.OfType<TextContentBlock>().Single().Text;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _client?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _host?.Dispose();
    }

    private static async Task WaitUntilPersistedAsync(ITraceStore store, string traceId)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (await store.GetAsync(traceId, CancellationToken.None) is null)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Trace {traceId} was never persisted.");
            }

            await Task.Delay(20);
        }
    }

    private static async Task<IHost> StartHostAsync() =>
        await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(options => options.IncludePaths("/boom/**"));
                    services.AddNetApiFlowMcp();
                });
                webBuilder.Configure(app =>
                {
                    app.UseExceptionHandler(handler => handler.Run(context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        return Task.CompletedTask;
                    }));
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints
                            .MapGet("/boom/{id:int}", string (int id) => throw new InvalidOperationException(SecretLeakCases.ById(id).Message))
                            .WithNetApiFlowTracking();
                        endpoints.MapNetApiFlowMcp();
                    });
                });
            })
            .StartAsync();
}

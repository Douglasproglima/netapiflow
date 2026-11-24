#pragma warning disable MCP9006

using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using NetApiFlow.Mcp.Endpoints;

namespace NetApiFlow.Mcp.Tests;

public sealed class AppTools
{
    public const string Name = "app-tool";
    public const string Answer = "from the app";

    [McpServerTool(Name = Name)]
    [Description("A tool of the app itself.")]
    public static string AppTool() => Answer;
}

internal sealed record McpAppSetup
{
    public string Environment { get; init; } = Environments.Development;

    public bool Stateless { get; init; }

    public bool PerSessionExecutionContext { get; init; }

    public string? NetApiFlowBasePath { get; init; }

    public string PathBase { get; init; } = string.Empty;

    public string[] AppPatterns { get; init; } = ["/mcp"];

    public Func<HttpContext, McpServerOptions, CancellationToken, Task>? ConfigureSessionOptions { get; init; }

    public ILoggerProvider? Logs { get; init; }

    public (Stream Input, Stream Output)? Streams { get; init; }
}

internal sealed class McpAppHost : IAsyncDisposable
{
    public static readonly string[] NetApiFlowTools = ["get_component_stats", "get_trace", "list_recent_traces"];

    private readonly IHost _host;
    private readonly string _pathBase;
    private int _nextId;

    private McpAppHost(IHost host, string pathBase)
    {
        _host = host;
        _pathBase = pathBase;
        Http = host.GetTestServer().CreateClient();
    }

    public HttpClient Http { get; }

    public static async Task<McpAppHost> StartAsync(McpAppSetup setup)
    {
        var host = await Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                if (setup.Logs is not null)
                {
                    logging.AddProvider(setup.Logs);
                }
            })
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(setup.Environment);
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    var mcp = services.AddMcpServer()
                        .WithHttpTransport(options =>
                        {
                            options.Stateless = setup.Stateless;
                            options.PerSessionExecutionContext = setup.PerSessionExecutionContext;
                            if (setup.ConfigureSessionOptions is not null)
                            {
                                options.ConfigureSessionOptions = setup.ConfigureSessionOptions;
                            }
                        })
                        .WithTools<AppTools>();
                    if (setup.Streams is { } streams)
                    {
                        mcp.WithStreamServerTransport(streams.Input, streams.Output);
                    }

                    services.AddNetApiFlow();
                    services.AddNetApiFlowMcp();
                });
                webBuilder.Configure(app =>
                {
                    if (setup.PathBase.Length > 0)
                    {
                        app.UsePathBase(setup.PathBase);
                    }

                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        foreach (var pattern in setup.AppPatterns)
                        {
                            endpoints.MapMcp(pattern);
                        }

                        if (setup.NetApiFlowBasePath is null)
                        {
                            endpoints.MapNetApiFlowMcp();
                        }
                        else
                        {
                            endpoints.MapNetApiFlowMcp(setup.NetApiFlowBasePath);
                        }
                    });
                });
            })
            .StartAsync();

        return new McpAppHost(host, setup.PathBase);
    }

    public async Task<McpAnswer> InitializeAsync(string path)
    {
        var answer = await SendAsync(path, null, "initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "netapiflow-tests", version = "1" },
        });
        if (answer.Status == 200)
        {
            await SendAsync(path, answer.SessionId, "notifications/initialized", null, isNotification: true);
        }

        return answer;
    }

    public Task<McpAnswer> ListToolsAsync(string path, string? sessionId) => SendAsync(path, sessionId, "tools/list", null);

    public Task<McpAnswer> CallToolAsync(string path, string? sessionId, string tool) =>
        SendAsync(path, sessionId, "tools/call", new { name = tool, arguments = new { } });

    public async Task<McpAnswer> SendAsync(string path, string? sessionId, string method, object? parameters, bool isNotification = false)
    {
        var message = new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["method"] = method };
        if (!isNotification)
        {
            message["id"] = Interlocked.Increment(ref _nextId);
        }

        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _pathBase + path)
        {
            Content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (sessionId is not null)
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var returnedSession = response.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.First() : sessionId;
        return new McpAnswer((int)response.StatusCode, McpAnswer.ParseMessage(body), returnedSession);
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}

internal sealed record McpAnswer(int Status, JsonElement? Message, string? SessionId)
{
    public int? ErrorCode => Message is { } message && message.TryGetProperty("error", out var error) ? error.GetProperty("code").GetInt32() : null;

    public string? ErrorMessage => Message is { } message && message.TryGetProperty("error", out var error) ? error.GetProperty("message").GetString() : null;

    public string[] ToolNames =>
        Message is { } message && message.TryGetProperty("result", out var result) && result.TryGetProperty("tools", out var tools)
            ? [.. tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()!).Order()]
            : [];

    public string? FirstText =>
        Message is { } message && message.TryGetProperty("result", out var result) && result.TryGetProperty("content", out var content)
            ? content.EnumerateArray().First().GetProperty("text").GetString()
            : null;

    public string? Instructions =>
        Message is { } message && message.TryGetProperty("result", out var result) && result.TryGetProperty("instructions", out var instructions)
            ? instructions.GetString()
            : null;

    public static JsonElement? ParseMessage(string body)
    {
        var json = body.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal)) is { } data
            ? data["data:".Length..].Trim()
            : body.Trim();
        return json.StartsWith('{') ? JsonDocument.Parse(json).RootElement.Clone() : null;
    }
}

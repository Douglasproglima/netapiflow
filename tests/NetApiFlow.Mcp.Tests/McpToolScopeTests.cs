using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using Shouldly;

namespace NetApiFlow.Mcp.Tests;

public class McpToolScopeTests
{
    private const string UnknownTool = "Unknown tool";
    private const int InvalidParams = -32602;

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful)]
    [InlineData(HttpServerSessionMode.Stateless)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    public async Task AppEndpoint_ListsTheToolsOfTheAppAndNoneOfNetApiFlow(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode });
        var session = (await host.InitializeAsync("/mcp")).SessionId;

        (await host.ListToolsAsync("/mcp", session)).ToolNames.ShouldBe([AppTools.Name]);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful)]
    [InlineData(HttpServerSessionMode.Stateless)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    public async Task AppEndpoint_CallingAToolOfNetApiFlow_AnswersUnknownTool(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode });
        var session = (await host.InitializeAsync("/mcp")).SessionId;

        var answer = await host.CallToolAsync("/mcp", session, "list_recent_traces");

        answer.ErrorCode.ShouldBe(InvalidParams);
        answer.ErrorMessage.ShouldNotBeNull().ShouldContain(UnknownTool);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful)]
    [InlineData(HttpServerSessionMode.Stateless)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    public async Task AppEndpoint_StillCallsItsOwnTool(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode });
        var session = (await host.InitializeAsync("/mcp")).SessionId;

        (await host.CallToolAsync("/mcp", session, AppTools.Name)).FirstText.ShouldBe(AppTools.Answer);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful)]
    [InlineData(HttpServerSessionMode.Stateless)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    public async Task AppEndpoint_KeepsTheSessionOptionsTheAppConfigured(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup
        {
            SessionMode = mode,
            ConfigureSessionOptions = (_, options, _) =>
            {
                options.ServerInstructions = "instructions of the app";
                return Task.CompletedTask;
            },
        });

        (await host.InitializeAsync("/mcp")).Instructions.ShouldBe("instructions of the app");
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful)]
    [InlineData(HttpServerSessionMode.Stateless)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    public async Task NetApiFlowEndpoint_ListsOnlyItsThreeToolsAndStillAnswersThem(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode });
        var session = (await host.InitializeAsync("/flow/mcp")).SessionId;

        (await host.ListToolsAsync("/flow/mcp", session)).ToolNames.ShouldBe(McpAppHost.NetApiFlowTools);
        (await host.CallToolAsync("/flow/mcp", session, "list_recent_traces")).FirstText.ShouldNotBeNull().ShouldStartWith("[");
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful)]
    [InlineData(HttpServerSessionMode.Stateless)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    public async Task NetApiFlowEndpoint_CallingTheToolOfTheApp_AnswersUnknownTool(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode });
        var session = (await host.InitializeAsync("/flow/mcp")).SessionId;

        var answer = await host.CallToolAsync("/flow/mcp", session, AppTools.Name);

        answer.ErrorCode.ShouldBe(InvalidParams);
        answer.ErrorMessage.ShouldNotBeNull().ShouldContain(UnknownTool);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful, "/interno/mcp", "")]
    [InlineData(HttpServerSessionMode.Stateful, null, "/api")]
    [InlineData(HttpServerSessionMode.Stateless, "/interno/mcp", "")]
    [InlineData(HttpServerSessionMode.Stateless, null, "/api")]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients, "/interno/mcp", "")]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients, null, "/api")]
    public async Task NetApiFlowEndpoint_UnderACustomBasePathOrAPathBase_ServesItsToolsAndTheAppEndpointDoesNot(HttpServerSessionMode mode, string? basePath, string pathBase)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode, NetApiFlowBasePath = basePath, PathBase = pathBase });
        var netApiFlowPath = basePath ?? "/flow/mcp";

        var netApiFlowSession = (await host.InitializeAsync(netApiFlowPath)).SessionId;
        var appSession = (await host.InitializeAsync("/mcp")).SessionId;

        (await host.ListToolsAsync(netApiFlowPath, netApiFlowSession)).ToolNames.ShouldBe(McpAppHost.NetApiFlowTools);
        (await host.ListToolsAsync("/mcp", appSession)).ToolNames.ShouldBe([AppTools.Name]);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful, "/flow/mcp-app")]
    [InlineData(HttpServerSessionMode.Stateful, "/flow/mcpx")]
    [InlineData(HttpServerSessionMode.Stateless, "/flow/mcp-app")]
    [InlineData(HttpServerSessionMode.Stateless, "/flow/mcpx")]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients, "/flow/mcp-app")]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients, "/flow/mcpx")]
    public async Task AppEndpoint_WhosePathStartsWithTheNetApiFlowPath_GetsNoneOfItsTools(HttpServerSessionMode mode, string appPattern)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode, AppPatterns = [appPattern] });
        var session = (await host.InitializeAsync(appPattern)).SessionId;

        (await host.ListToolsAsync(appPattern, session)).ToolNames.ShouldBe([AppTools.Name]);
        (await host.CallToolAsync(appPattern, session, "get_trace")).ErrorCode.ShouldBe(InvalidParams);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful)]
    [InlineData(HttpServerSessionMode.Stateless)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    public async Task SessionOpenedOnTheNetApiFlowEndpoint_ReusedOnTheAppEndpoint_ReachesNoneOfItsTools(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode });
        var session = (await host.InitializeAsync("/flow/mcp")).SessionId;

        (await host.ListToolsAsync("/mcp", session)).ToolNames.ShouldBe([AppTools.Name]);
        (await host.CallToolAsync("/mcp", session, "list_recent_traces")).ErrorCode.ShouldBe(InvalidParams);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful)]
    [InlineData(HttpServerSessionMode.Stateless)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    public async Task SessionOpenedOnTheAppEndpoint_ReusedOnTheNetApiFlowEndpoint_FollowsItsAccessRules(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode, Environment = Environments.Production });
        var session = (await host.InitializeAsync("/mcp")).SessionId;

        (await host.ListToolsAsync("/flow/mcp", session)).Status.ShouldBe(404);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    public async Task WithPerSessionExecutionContextAndSessions_TheToolsOfNetApiFlowAreServedOnNoEndpointAndOneWarningSaysWhy(HttpServerSessionMode mode)
    {
        var logs = new WarningLogs();
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode, PerSessionExecutionContext = true, Logs = logs });

        var appSession = (await host.InitializeAsync("/mcp")).SessionId;
        var netApiFlowInitialize = await host.InitializeAsync("/flow/mcp");

        (await host.ListToolsAsync("/mcp", appSession)).ToolNames.ShouldBe([AppTools.Name]);
        (await host.CallToolAsync("/mcp", appSession, "list_recent_traces")).ErrorCode.ShouldBe(InvalidParams);
        netApiFlowInitialize.Status.ShouldBe(404);
        logs.Warnings.Count(warning => warning.Contains("PerSessionExecutionContext", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task WithPerSessionExecutionContextInStatelessMode_TheNetApiFlowEndpointStillServesItsToolsWithNoWarning()
    {
        var logs = new WarningLogs();
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = HttpServerSessionMode.Stateless, PerSessionExecutionContext = true, Logs = logs });

        (await host.ListToolsAsync("/flow/mcp", null)).ToolNames.ShouldBe(McpAppHost.NetApiFlowTools);
        (await host.ListToolsAsync("/mcp", null)).ToolNames.ShouldBe([AppTools.Name]);
        logs.Warnings.ShouldNotContain(warning => warning.Contains("PerSessionExecutionContext", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful)]
    [InlineData(HttpServerSessionMode.Stateless)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients)]
    public async Task WithoutPerSessionExecutionContext_NoWarningAboutItIsLogged(HttpServerSessionMode mode)
    {
        var logs = new WarningLogs();
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode, Logs = logs });

        await host.InitializeAsync("/flow/mcp");

        logs.Warnings.ShouldNotContain(warning => warning.Contains("PerSessionExecutionContext", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ToolsListOverATransportWithNoHttpRequest_ListsNoneOfTheToolsOfNetApiFlow()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        await using var host = await McpAppHost.StartAsync(new McpAppSetup
        {
            Streams = (clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()),
        });
        await using var input = clientToServer.Writer.AsStream();
        using var output = new StreamReader(serverToClient.Reader.AsStream(), Encoding.UTF8);

        await WriteLineAsync(input, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"stream","version":"1"}}}""");
        await ReadAnswerAsync(output, 1);
        await WriteLineAsync(input, """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        await WriteLineAsync(input, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var tools = await ReadAnswerAsync(output, 2);

        new McpAnswer(200, tools, null).ToolNames.ShouldBe([AppTools.Name]);
    }

    private static async Task WriteLineAsync(Stream input, string line)
    {
        await input.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
        await input.FlushAsync();
    }

    private static async Task<JsonElement> ReadAnswerAsync(StreamReader output, int id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var line = await output.ReadLineAsync(timeout.Token) ?? throw new InvalidOperationException("The stream ended before the answer.");
            var message = JsonDocument.Parse(line).RootElement.Clone();
            if (message.TryGetProperty("id", out var answerId) && answerId.GetInt32() == id)
            {
                return message;
            }
        }
    }

    private sealed class WarningLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        public IReadOnlyList<string> Warnings => [.. _warnings];

        public ILogger CreateLogger(string categoryName) => new WarningLogger(_warnings);

        public void Dispose()
        {
        }

        private sealed class WarningLogger(ConcurrentQueue<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    warnings.Enqueue(formatter(state, exception));
                }
            }
        }
    }
}

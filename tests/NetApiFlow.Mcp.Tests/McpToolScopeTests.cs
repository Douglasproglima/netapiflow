using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace NetApiFlow.Mcp.Tests;

public class McpToolScopeTests
{
    private const string UnknownTool = "Unknown tool";
    private const int InvalidParams = -32602;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppEndpoint_ListsTheToolsOfTheAppAndNoneOfNetApiFlow(bool stateless)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { Stateless = stateless });
        var session = (await host.InitializeAsync("/mcp")).SessionId;

        (await host.ListToolsAsync("/mcp", session)).ToolNames.ShouldBe([AppTools.Name]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppEndpoint_CallingAToolOfNetApiFlow_AnswersUnknownTool(bool stateless)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { Stateless = stateless });
        var session = (await host.InitializeAsync("/mcp")).SessionId;

        var answer = await host.CallToolAsync("/mcp", session, "list_recent_traces");

        answer.ErrorCode.ShouldBe(InvalidParams);
        answer.ErrorMessage.ShouldNotBeNull().ShouldContain(UnknownTool);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppEndpoint_StillCallsItsOwnTool(bool stateless)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { Stateless = stateless });
        var session = (await host.InitializeAsync("/mcp")).SessionId;

        (await host.CallToolAsync("/mcp", session, AppTools.Name)).FirstText.ShouldBe(AppTools.Answer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppEndpoint_KeepsTheSessionOptionsTheAppConfigured(bool stateless)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup
        {
            Stateless = stateless,
            ConfigureSessionOptions = (_, options, _) =>
            {
                options.ServerInstructions = "instructions of the app";
                return Task.CompletedTask;
            },
        });

        (await host.InitializeAsync("/mcp")).Instructions.ShouldBe("instructions of the app");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetApiFlowEndpoint_ListsOnlyItsThreeToolsAndStillAnswersThem(bool stateless)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { Stateless = stateless });
        var session = (await host.InitializeAsync("/flow/mcp")).SessionId;

        (await host.ListToolsAsync("/flow/mcp", session)).ToolNames.ShouldBe(McpAppHost.NetApiFlowTools);
        (await host.CallToolAsync("/flow/mcp", session, "list_recent_traces")).FirstText.ShouldNotBeNull().ShouldStartWith("[");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetApiFlowEndpoint_CallingTheToolOfTheApp_AnswersUnknownTool(bool stateless)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { Stateless = stateless });
        var session = (await host.InitializeAsync("/flow/mcp")).SessionId;

        var answer = await host.CallToolAsync("/flow/mcp", session, AppTools.Name);

        answer.ErrorCode.ShouldBe(InvalidParams);
        answer.ErrorMessage.ShouldNotBeNull().ShouldContain(UnknownTool);
    }

    [Theory]
    [InlineData(false, "/interno/mcp", "")]
    [InlineData(true, "/interno/mcp", "")]
    [InlineData(false, null, "/api")]
    [InlineData(true, null, "/api")]
    public async Task NetApiFlowEndpoint_UnderACustomBasePathOrAPathBase_ServesItsToolsAndTheAppEndpointDoesNot(bool stateless, string? basePath, string pathBase)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { Stateless = stateless, NetApiFlowBasePath = basePath, PathBase = pathBase });
        var netApiFlowPath = basePath ?? "/flow/mcp";

        var netApiFlowSession = (await host.InitializeAsync(netApiFlowPath)).SessionId;
        var appSession = (await host.InitializeAsync("/mcp")).SessionId;

        (await host.ListToolsAsync(netApiFlowPath, netApiFlowSession)).ToolNames.ShouldBe(McpAppHost.NetApiFlowTools);
        (await host.ListToolsAsync("/mcp", appSession)).ToolNames.ShouldBe([AppTools.Name]);
    }

    [Theory]
    [InlineData(false, "/flow/mcp-app")]
    [InlineData(true, "/flow/mcp-app")]
    [InlineData(false, "/flow/mcpx")]
    [InlineData(true, "/flow/mcpx")]
    public async Task AppEndpoint_WhosePathStartsWithTheNetApiFlowPath_GetsNoneOfItsTools(bool stateless, string appPattern)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { Stateless = stateless, AppPatterns = [appPattern] });
        var session = (await host.InitializeAsync(appPattern)).SessionId;

        (await host.ListToolsAsync(appPattern, session)).ToolNames.ShouldBe([AppTools.Name]);
        (await host.CallToolAsync(appPattern, session, "get_trace")).ErrorCode.ShouldBe(InvalidParams);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionOpenedOnTheNetApiFlowEndpoint_ReusedOnTheAppEndpoint_ReachesNoneOfItsTools(bool stateless)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { Stateless = stateless });
        var session = (await host.InitializeAsync("/flow/mcp")).SessionId;

        (await host.ListToolsAsync("/mcp", session)).ToolNames.ShouldBe([AppTools.Name]);
        (await host.CallToolAsync("/mcp", session, "list_recent_traces")).ErrorCode.ShouldBe(InvalidParams);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionOpenedOnTheAppEndpoint_ReusedOnTheNetApiFlowEndpoint_FollowsItsAccessRules(bool stateless)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { Stateless = stateless, Environment = Environments.Production });
        var session = (await host.InitializeAsync("/mcp")).SessionId;

        (await host.ListToolsAsync("/flow/mcp", session)).Status.ShouldBe(404);
    }

    [Fact]
    public async Task WithPerSessionExecutionContext_TheToolsOfNetApiFlowAreServedOnNoEndpointAndOneWarningSaysWhy()
    {
        var logs = new WarningLogs();
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { PerSessionExecutionContext = true, Logs = logs });

        var appSession = (await host.InitializeAsync("/mcp")).SessionId;
        var netApiFlowInitialize = await host.InitializeAsync("/flow/mcp");

        (await host.ListToolsAsync("/mcp", appSession)).ToolNames.ShouldBe([AppTools.Name]);
        (await host.CallToolAsync("/mcp", appSession, "list_recent_traces")).ErrorCode.ShouldBe(InvalidParams);
        netApiFlowInitialize.Status.ShouldBe(404);
        logs.Warnings.Count(warning => warning.Contains("PerSessionExecutionContext", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutPerSessionExecutionContext_NoWarningAboutItIsLogged(bool stateless)
    {
        var logs = new WarningLogs();
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { Stateless = stateless, Logs = logs });

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

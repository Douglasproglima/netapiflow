#pragma warning disable MCPEXP002

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shouldly;

namespace NetApiFlow.Mcp.Tests;

public class McpAppServerCompatibilityTests
{
    private const string AlternateAnswer = "from the alternate handler of the app";

    [Theory]
    [MemberData(nameof(McpSessionModes.All), MemberType = typeof(McpSessionModes))]
    public async Task AppWithAnAlternateCallToolHandler_BothEndpointsStillWork(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup
        {
            SessionMode = mode,
            ConfigureAppServer = mcp => mcp.Services.Configure<McpServerOptions>(options =>
                options.Handlers.CallToolWithAlternateHandler = (_, _) =>
                    ValueTask.FromResult(new ResultOrAlternate<CallToolResult>(new CallToolResult { Content = [new TextContentBlock { Text = AlternateAnswer }] }))),
        });
        await using var app = await host.ConnectAsync("/mcp");
        await using var netApiFlow = await host.ConnectAsync("/flow/mcp");

        (await McpToolScopeWithTheOfficialClientTests.ToolNamesAsync(app)).ShouldBe([AppTools.Name]);
        (await McpToolScopeWithTheOfficialClientTests.TextOfAsync(app, AppTools.Name)).ShouldBeOneOf(AppTools.Answer, AlternateAnswer);
        (await McpToolScopeWithTheOfficialClientTests.ToolNamesAsync(netApiFlow)).ShouldBe(McpAppHost.NetApiFlowTools);
        (await McpToolScopeWithTheOfficialClientTests.TextOfAsync(netApiFlow, "list_recent_traces")).ShouldStartWith("[");
    }

    [Theory]
    [MemberData(nameof(McpSessionModes.All), MemberType = typeof(McpSessionModes))]
    public async Task AppToolNamedLikeANetApiFlowToolAndRegisteredFirst_IsServedOnlyOnTheAppEndpointAndOneWarningSaysSo(HttpServerSessionMode mode)
    {
        var logs = new WarningLogs();
        await using var host = await McpAppHost.StartAsync(new McpAppSetup
        {
            SessionMode = mode,
            Logs = logs,
            ConfigureAppServer = mcp => mcp.WithTools<AppGetTrace>(),
        });
        await using var app = await host.ConnectAsync("/mcp");
        await using var netApiFlow = await host.ConnectAsync("/flow/mcp");

        (await McpToolScopeWithTheOfficialClientTests.ToolNamesAsync(netApiFlow)).ShouldBe(["get_component_stats", "list_recent_traces"]);
        (await Should.ThrowAsync<McpProtocolException>(() => netApiFlow.CallToolAsync("get_trace", cancellationToken: CancellationToken.None).AsTask()))
            .ErrorCode.ShouldBe(McpErrorCode.InvalidParams);
        (await McpToolScopeWithTheOfficialClientTests.ToolNamesAsync(app)).ShouldBe([AppTools.Name, "get_trace"]);
        (await McpToolScopeWithTheOfficialClientTests.TextOfAsync(app, "get_trace")).ShouldBe(AppGetTrace.Answer);
        logs.Warnings.Count(warning => warning.Contains("'get_trace'", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(McpSessionModes.All), MemberType = typeof(McpSessionModes))]
    public async Task WithoutAnAppToolNamedLikeANetApiFlowTool_NoWarningAboutNamesIsLogged(HttpServerSessionMode mode)
    {
        var logs = new WarningLogs();
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode, Logs = logs });
        await using var netApiFlow = await host.ConnectAsync("/flow/mcp");

        (await McpToolScopeWithTheOfficialClientTests.ToolNamesAsync(netApiFlow)).ShouldBe(McpAppHost.NetApiFlowTools);
        logs.Warnings.ShouldNotContain(warning => warning.Contains("get_", StringComparison.Ordinal) || warning.Contains("list_recent_traces", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(McpSessionModes.WithSessions), MemberType = typeof(McpSessionModes))]
    public async Task OverTheLegacySseTransport_EachEndpointServesOnlyItsOwnTools(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode, LegacySse = true });
        await using var app = await host.ConnectAsync("/mcp/sse", HttpTransportMode.Sse);
        await using var netApiFlow = await host.ConnectAsync("/flow/mcp/sse", HttpTransportMode.Sse);

        (await McpToolScopeWithTheOfficialClientTests.ToolNamesAsync(app)).ShouldBe([AppTools.Name]);
        (await McpToolScopeWithTheOfficialClientTests.ToolNamesAsync(netApiFlow)).ShouldBe(McpAppHost.NetApiFlowTools);
        (await Should.ThrowAsync<McpProtocolException>(() => netApiFlow.CallToolAsync(AppTools.Name, cancellationToken: CancellationToken.None).AsTask()))
            .ErrorCode.ShouldBe(McpErrorCode.InvalidParams);
        (await Should.ThrowAsync<McpProtocolException>(() => app.CallToolAsync("list_recent_traces", cancellationToken: CancellationToken.None).AsTask()))
            .ErrorCode.ShouldBe(McpErrorCode.InvalidParams);
    }

    [Theory]
    [MemberData(nameof(McpSessionModes.All), MemberType = typeof(McpSessionModes))]
    public async Task WhenTheAppClearsTheHttpContextBeforeNetApiFlowSeesTheMessage_EachEndpointStillServesOnlyItsOwnTools(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup
        {
            SessionMode = mode,
            ConfigureAppServer = mcp =>
            {
                mcp.Services.AddHttpContextAccessor();
                mcp.WithMessageFilters(filters => filters.AddIncomingFilter(next => async (context, cancellationToken) =>
                {
                    var accessor = context.Services?.GetService<IHttpContextAccessor>();
                    if (accessor is not null)
                    {
                        accessor.HttpContext = null;
                    }

                    await next(context, cancellationToken);
                }));
            },
        });
        await using var app = await host.ConnectAsync("/mcp");
        await using var netApiFlow = await host.ConnectAsync("/flow/mcp");

        (await McpToolScopeWithTheOfficialClientTests.ToolNamesAsync(netApiFlow)).ShouldBe(McpAppHost.NetApiFlowTools);
        (await McpToolScopeWithTheOfficialClientTests.TextOfAsync(netApiFlow, "list_recent_traces")).ShouldStartWith("[");
        (await Should.ThrowAsync<McpProtocolException>(() => netApiFlow.CallToolAsync(AppTools.Name, cancellationToken: CancellationToken.None).AsTask()))
            .ErrorCode.ShouldBe(McpErrorCode.InvalidParams);
        (await McpToolScopeWithTheOfficialClientTests.ToolNamesAsync(app)).ShouldBe([AppTools.Name]);
    }

    [Theory]
    [MemberData(nameof(McpSessionModes.All), MemberType = typeof(McpSessionModes))]
    public async Task RequestRefusedOnTheNetApiFlowEndpoint_LeavesNothingThatRunsAfterItTreatedAsComingFromThere(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode, Environment = Environments.Production });

        (await host.InitializeAsync("/flow/mcp")).Status.ShouldBe(404);
        var session = (await host.InitializeAsync("/mcp")).SessionId;

        (await host.ListToolsAsync("/mcp", session)).ToolNames.ShouldBe([AppTools.Name]);
        (await host.CallToolAsync("/mcp", session, "list_recent_traces")).ErrorCode.ShouldBe((int)McpErrorCode.InvalidParams);
    }

    [Theory]
    [MemberData(nameof(McpSessionModes.All), MemberType = typeof(McpSessionModes))]
    public async Task AppWithoutAServerName_KeepsItsDefaultNameAndTheNetApiFlowEndpointIsCalledNetApiFlow(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode });
        await using var app = await host.ConnectAsync("/mcp");
        await using var netApiFlow = await host.ConnectAsync("/flow/mcp");

        app.ServerInfo.ShouldNotBeNull().Name.ShouldNotBe("NetApiFlow");
        (await host.InitializeAsync("/mcp")).ServerName.ShouldNotBe("NetApiFlow");
        netApiFlow.ServerInfo.ShouldNotBeNull().Name.ShouldBe("NetApiFlow");
        (await host.InitializeAsync("/flow/mcp")).ServerName.ShouldBe("NetApiFlow");
    }

    [Theory]
    [MemberData(nameof(McpSessionModes.All), MemberType = typeof(McpSessionModes))]
    public async Task AppWithItsOwnServerName_KeepsItAndTheNetApiFlowEndpointIsCalledNetApiFlow(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup
        {
            SessionMode = mode,
            ConfigureAppServer = mcp => mcp.Services.Configure<McpServerOptions>(options => options.ServerInfo = new Implementation { Name = "MyApp", Version = "1.0.0" }),
        });
        await using var app = await host.ConnectAsync("/mcp");
        await using var netApiFlow = await host.ConnectAsync("/flow/mcp");

        app.ServerInfo.ShouldNotBeNull().Name.ShouldBe("MyApp");
        netApiFlow.ServerInfo.ShouldNotBeNull().Name.ShouldBe("NetApiFlow");
        (await host.InitializeAsync("/flow/mcp")).ServerName.ShouldBe("NetApiFlow");
    }

    internal sealed class WarningLogs : ILoggerProvider
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

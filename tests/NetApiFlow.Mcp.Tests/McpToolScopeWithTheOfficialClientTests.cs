using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;

namespace NetApiFlow.Mcp.Tests;

public class McpToolScopeWithTheOfficialClientTests
{
    [Theory]
    [MemberData(nameof(McpSessionModes.All), MemberType = typeof(McpSessionModes))]
    public async Task AppEndpoint_ListsAndCallsOnlyTheToolsOfTheApp(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode });
        await using var client = await host.ConnectAsync("/mcp");

        (await ToolNamesAsync(client)).ShouldBe([AppTools.Name]);
        (await TextOfAsync(client, AppTools.Name)).ShouldBe(AppTools.Answer);
        (await RefusalAsync(client, "list_recent_traces")).ShouldBe(McpErrorCode.InvalidParams);
    }

    [Theory]
    [MemberData(nameof(McpSessionModes.All), MemberType = typeof(McpSessionModes))]
    public async Task NetApiFlowEndpoint_ListsAndCallsOnlyItsThreeTools(HttpServerSessionMode mode)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode });
        await using var client = await host.ConnectAsync("/flow/mcp");

        (await ToolNamesAsync(client)).ShouldBe(McpAppHost.NetApiFlowTools);
        (await TextOfAsync(client, "list_recent_traces")).ShouldStartWith("[");
        (await RefusalAsync(client, AppTools.Name)).ShouldBe(McpErrorCode.InvalidParams);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateful, "/interno/mcp", "")]
    [InlineData(HttpServerSessionMode.Stateful, null, "/api")]
    [InlineData(HttpServerSessionMode.Stateless, "/interno/mcp", "")]
    [InlineData(HttpServerSessionMode.Stateless, null, "/api")]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients, "/interno/mcp", "")]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients, null, "/api")]
    public async Task UnderACustomBasePathOrAPathBase_EachEndpointServesOnlyItsOwnTools(HttpServerSessionMode mode, string? basePath, string pathBase)
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = mode, NetApiFlowBasePath = basePath, PathBase = pathBase });
        await using var netApiFlow = await host.ConnectAsync(basePath ?? "/flow/mcp");
        await using var app = await host.ConnectAsync("/mcp");

        (await ToolNamesAsync(netApiFlow)).ShouldBe(McpAppHost.NetApiFlowTools);
        (await RefusalAsync(netApiFlow, AppTools.Name)).ShouldBe(McpErrorCode.InvalidParams);
        (await ToolNamesAsync(app)).ShouldBe([AppTools.Name]);
        (await RefusalAsync(app, "get_trace")).ShouldBe(McpErrorCode.InvalidParams);
    }

    [Fact]
    public async Task SessionOpenedOnTheNetApiFlowEndpoint_ReusedOnTheAppEndpoint_ReachesNoneOfItsTools()
    {
        await using var host = await McpAppHost.StartAsync(new McpAppSetup { SessionMode = HttpServerSessionMode.Stateful });
        await using var client = await host.ConnectAsync("/flow/mcp");
        var session = client.SessionId.ShouldNotBeNull();

        (await host.ListToolsAsync("/mcp", session)).ToolNames.ShouldBe([AppTools.Name]);
        (await host.CallToolAsync("/mcp", session, "list_recent_traces")).ErrorCode.ShouldBe((int)McpErrorCode.InvalidParams);
    }

    internal static async Task<string[]> ToolNamesAsync(McpClient client) =>
        [.. (await client.ListToolsAsync(cancellationToken: CancellationToken.None)).Select(tool => tool.Name).Order()];

    internal static async Task<string> TextOfAsync(McpClient client, string tool)
    {
        var result = await client.CallToolAsync(tool, cancellationToken: CancellationToken.None);
        result.IsError.ShouldNotBe(true);
        return result.Content.OfType<TextContentBlock>().First().Text;
    }

    private static async Task<McpErrorCode> RefusalAsync(McpClient client, string tool) =>
        (await Should.ThrowAsync<McpProtocolException>(() => client.CallToolAsync(tool, cancellationToken: CancellationToken.None).AsTask())).ErrorCode;
}

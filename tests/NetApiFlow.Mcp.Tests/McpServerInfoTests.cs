using Shouldly;

namespace NetApiFlow.Mcp.Tests;

public class McpServerInfoTests
{
    [Fact]
    public async Task Initialize_AnswersWithNetApiFlowAsTheServerName()
    {
        await using var fixture = await McpTestFixture.StartAsync();

        fixture.Client.ServerInfo.ShouldNotBeNull().Name.ShouldBe("NetApiFlow");
    }

    [Fact]
    public async Task ToolsList_AnswersTheThreeToolsByName()
    {
        await using var fixture = await McpTestFixture.StartAsync();

        var tools = await fixture.Client.ListToolsAsync(cancellationToken: CancellationToken.None);

        tools.Select(tool => tool.Name).Order().ShouldBe(["get_component_stats", "get_trace", "list_recent_traces"]);
    }
}

using System.Text.Json;
using NetApiFlow.Domain;
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

    [Theory]
    [InlineData("/api/orders/{id}")]
    [InlineData("/api/orders/{id:int}")]
    [InlineData("api/orders/{id}")]
    public async Task ListRecentTraces_FilteredByTheRouteWithOrWithoutTheConstraint_FindsTheTraceRecordedWithTheConstraint(string route)
    {
        await using var fixture = await McpTestFixture.StartAsync();
        var constrained = CreateTrace("trace-1", "GET", FlowStatus.Success);
        await fixture.Store.SaveAsync(constrained with { RouteTemplate = "/api/orders/{id:int}" }, [], CancellationToken.None);
        await fixture.Store.SaveAsync(constrained with { TraceId = "trace-2", RouteTemplate = "/api/customers/{id:int}" }, [], CancellationToken.None);

        var result = await fixture.Client.CallToolAsync(
            "list_recent_traces",
            new Dictionary<string, object?> { ["route"] = route },
            cancellationToken: CancellationToken.None);

        var traces = ReadTraces(result);
        traces.Count.ShouldBe(1);
        traces[0].GetProperty("traceId").GetString().ShouldBe("trace-1");
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
}

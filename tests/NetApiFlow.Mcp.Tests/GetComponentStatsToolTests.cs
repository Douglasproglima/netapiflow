using System.Text.Json;
using NetApiFlow.Domain;
using Shouldly;
using Xunit;

namespace NetApiFlow.Mcp.Tests;

public class GetComponentStatsToolTests
{
    [Fact]
    public async Task GetComponentStats_WithKnownSpans_ReturnsAggregatedStats()
    {
        await using var fixture = await McpTestFixture.StartAsync();
        var startedAt = DateTime.UtcNow;

        await fixture.Store.SaveAsync(
            new FlowTrace
            {
                TraceId = "trace-1",
                Method = "GET",
                RouteTemplate = "/api/orders/{id}",
                Status = FlowStatus.Success,
                StartedAt = startedAt,
            },
            [
                new FlowSpan
                {
                    TraceId = "trace-1",
                    SpanId = "span-1",
                    Sequence = 0,
                    Component = "OrderRepository",
                    Operation = "GetById",
                    Type = ComponentType.Repository,
                    StartedAt = startedAt,
                    DurationMicros = 1000,
                    Status = FlowStatus.Success,
                },
                new FlowSpan
                {
                    TraceId = "trace-1",
                    SpanId = "span-2",
                    Sequence = 1,
                    Component = "OrderRepository",
                    Operation = "GetById",
                    Type = ComponentType.Repository,
                    StartedAt = startedAt,
                    DurationMicros = 2000,
                    Status = FlowStatus.Error,
                },
            ],
            CancellationToken.None);

        var result = await fixture.Client.CallToolAsync("get_component_stats", cancellationToken: CancellationToken.None);

        var stats = ReadStats(result);
        stats.Count.ShouldBe(1);
        stats[0].GetProperty("component").GetString().ShouldBe("OrderRepository");
        stats[0].GetProperty("executionCount").GetInt32().ShouldBe(2);
        stats[0].GetProperty("errorCount").GetInt32().ShouldBe(1);
    }

    private static List<JsonElement> ReadStats(ModelContextProtocol.Protocol.CallToolResult result)
    {
        var text = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text;
        return [.. JsonDocument.Parse(text).RootElement.EnumerateArray()];
    }
}

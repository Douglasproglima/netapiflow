using System.Text.Json;
using NetApiFlow.Domain;
using Shouldly;
using Xunit;

namespace NetApiFlow.Mcp.Tests;

public class GetTraceToolTests
{
    [Fact]
    public async Task GetTrace_WithExistingTrace_ReturnsFoundWithTrace()
    {
        await using var fixture = await McpTestFixture.StartAsync();
        await fixture.Store.SaveAsync(
            new FlowTrace
            {
                TraceId = "trace-1",
                Method = "GET",
                RouteTemplate = "/api/orders/{id}",
                Status = FlowStatus.Success,
                StartedAt = DateTime.UtcNow,
            },
            [],
            CancellationToken.None);

        var result = await fixture.Client.CallToolAsync(
            "get_trace",
            new Dictionary<string, object?> { ["traceId"] = "trace-1" },
            cancellationToken: CancellationToken.None);

        var payload = ReadPayload(result);
        payload.GetProperty("found").GetBoolean().ShouldBeTrue();
        payload.GetProperty("trace").GetProperty("trace").GetProperty("traceId").GetString().ShouldBe("trace-1");
    }

    [Fact]
    public async Task GetTrace_WithSpanLogs_ReturnsLogsWithoutFiltering()
    {
        await using var fixture = await McpTestFixture.StartAsync();
        await fixture.Store.SaveAsync(
            new FlowTrace
            {
                TraceId = "trace-logs",
                Method = "GET",
                RouteTemplate = "/api/orders/{id}",
                Status = FlowStatus.Success,
                StartedAt = DateTime.UtcNow,
            },
            [
                new FlowSpan
                {
                    TraceId = "trace-logs",
                    SpanId = "span-1",
                    Sequence = 0,
                    Component = "OrderService",
                    Operation = "GetAsync",
                    Type = ComponentType.Service,
                    StartedAt = DateTime.UtcNow,
                    Status = FlowStatus.Success,
                    Logs =
                    [
                        new LogEntry
                        {
                            Timestamp = DateTime.UtcNow,
                            Level = "Warning",
                            Message = "stock running low",
                        },
                    ],
                },
            ],
            CancellationToken.None);

        var result = await fixture.Client.CallToolAsync(
            "get_trace",
            new Dictionary<string, object?> { ["traceId"] = "trace-logs" },
            cancellationToken: CancellationToken.None);

        var payload = ReadPayload(result);
        var logs = payload.GetProperty("trace").GetProperty("spans")[0].GetProperty("logs");
        logs.GetArrayLength().ShouldBe(1);
        logs[0].GetProperty("message").GetString().ShouldBe("stock running low");
    }

    [Fact]
    public async Task GetTrace_WithUnknownTraceId_ReturnsNotFoundWithoutThrowing()
    {
        await using var fixture = await McpTestFixture.StartAsync();

        var result = await fixture.Client.CallToolAsync(
            "get_trace",
            new Dictionary<string, object?> { ["traceId"] = "does-not-exist" },
            cancellationToken: CancellationToken.None);

        result.IsError.ShouldNotBe(true);

        var payload = ReadPayload(result);
        payload.GetProperty("found").GetBoolean().ShouldBeFalse();
    }

    private static JsonElement ReadPayload(ModelContextProtocol.Protocol.CallToolResult result)
    {
        var text = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text;
        return JsonDocument.Parse(text).RootElement;
    }
}

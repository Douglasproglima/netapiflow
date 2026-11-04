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

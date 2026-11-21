using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class RunningTraceDetailTests
{
    [Fact]
    public async Task TraceLookupOfThePanel_WhileTheRequestStillRuns_CarriesTheW3CTraceIdTag()
    {
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            Dashboard = true,
            Map = endpoints => endpoints.MapGet("/held", async () =>
            {
                inside.SetResult();
                await release.Task;
                return "ok";
            }),
        });
        var w3cTraceId = ActivityTraceId.CreateRandom().ToHexString();

        var held = app.GetWithTraceIdAsync("/held", w3cTraceId);
        await inside.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var traceId = await RunningTraceIdAsync(app);
        var lookup = await app.GetAsync($"/flow/api/traces/{traceId}");
        release.SetResult();
        await held;

        lookup.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var detail = JsonDocument.Parse(await lookup.Content.ReadAsStringAsync());
        detail.RootElement.GetProperty("status").GetString().ShouldBe("Running");
        traceId.ShouldNotBe(w3cTraceId);
        detail.RootElement.GetProperty("tags").GetProperty("w3c.trace_id").GetString().ShouldBe(w3cTraceId);
    }

    private static async Task<string> RunningTraceIdAsync(PathBaseApp app)
    {
        var live = await app.GetAsync("/flow/api/traces/live");
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var running = JsonDocument.Parse(await live.Content.ReadAsStringAsync());
        return running.RootElement
            .EnumerateArray()
            .Single(item => item.GetProperty("routeTemplate").GetString() == "/held")
            .GetProperty("traceId")
            .GetString()!;
    }
}

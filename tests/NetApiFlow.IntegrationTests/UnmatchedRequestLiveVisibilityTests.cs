using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class UnmatchedRequestLiveVisibilityTests
{
    [Fact]
    public async Task RequestWithoutAMatchedEndpoint_WhileItRuns_IsNotListedAsRunningAndItsStepWaitsForTheEnd()
    {
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            Dashboard = true,
            BeforeRouting = builder => builder.Map("/terminal", branch => branch.Run(async context =>
            {
                using (Flow.Step("TerminalMiddleware", "Work"))
                {
                    inside.SetResult();
                    await release.Task;
                }

                await context.Response.WriteAsync("done");
            })),
        });

        var slow = app.GetAsync("/terminal");
        await inside.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var live = await app.GetAsync("/flow/api/traces/live");
        var startedWhileRunning = app.StartedTraces.ToArray();
        var stepsWhileRunning = app.StartedSpans.ToArray();
        release.SetResult();
        var response = await slow;

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var running = JsonDocument.Parse(await live.Content.ReadAsStringAsync());
        running.RootElement.GetArrayLength().ShouldBe(0);
        startedWhileRunning.ShouldBeEmpty();
        stepsWhileRunning.ShouldBeEmpty();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = await app.StoredAsync(response);
        stored.Trace.RouteTemplate.ShouldBe("(unmatched)");
        stored.Spans.Select(span => span.Component).ShouldBe(["TerminalMiddleware"]);
        app.StartedTraces.Select(trace => trace.RouteTemplate).ShouldBe(["(unmatched)"]);
        app.StartedSpans.Select(span => span.Component).ShouldBe(["TerminalMiddleware"]);
    }

    [Fact]
    public async Task RequestWithAMatchedEndpoint_WhileItRuns_IsListedAsRunningWithItsRoute()
    {
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            Dashboard = true,
            Map = endpoints => endpoints.MapGet("/orders/{id:int}", async (int id) =>
            {
                inside.SetResult();
                await release.Task;
                return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }),
        });

        var slow = app.GetAsync("/orders/7");
        await inside.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var live = await app.GetAsync("/flow/api/traces/live");
        release.SetResult();
        await slow;

        using var running = JsonDocument.Parse(await live.Content.ReadAsStringAsync());
        running.RootElement.EnumerateArray().Select(item => item.GetProperty("routeTemplate").GetString()).ShouldBe(["/orders/{id:int}"]);
    }
}

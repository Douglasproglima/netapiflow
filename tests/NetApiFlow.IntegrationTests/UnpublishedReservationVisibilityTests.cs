using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Shouldly;
using Xunit;

namespace NetApiFlow.IntegrationTests;

public class UnpublishedReservationVisibilityTests
{
    private const string SharedTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task TraceLookupOfThePanel_WhileAnExcludedRequestUsesTheSameTraceId_ServesTheStoredTrace()
    {
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            Dashboard = true,
            Configure = options => options.ExcludePaths("/internal/**"),
            BeforeRouting = builder => builder.Map("/internal/slow", branch => branch.Run(async _ =>
            {
                inside.SetResult();
                await release.Task;
            })),
        });
        var tracked = await app.GetWithTraceIdAsync(PathBaseApp.ControlRoute, SharedTraceId);
        tracked.EnsureSuccessStatusCode();
        var traceId = tracked.Headers.GetValues(PathBaseApp.TraceIdHeader).Single();
        await app.StoredAsync(traceId);

        var excluded = app.GetWithTraceIdAsync("/internal/slow", SharedTraceId);
        await inside.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var lookup = await app.GetAsync($"/flow/api/traces/{traceId}");
        release.SetResult();
        await excluded;

        lookup.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var detail = JsonDocument.Parse(await lookup.Content.ReadAsStringAsync());
        detail.RootElement.GetProperty("routeTemplate").GetString().ShouldBe(PathBaseApp.ControlRoute);
        detail.RootElement.GetProperty("status").GetString().ShouldNotBe("Running");
    }

    [Fact]
    public async Task SpanLookupOfThePanel_WhileAnExcludedRequestUsesTheSameTraceId_ServesTheStoredSpans()
    {
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await PathBaseApp.StartAsync(new PathBaseSetup
        {
            Dashboard = true,
            Configure = options => options.ExcludePaths("/internal/**"),
            Map = endpoints => endpoints.MapGet("/orders", () =>
            {
                using (NetApiFlow.Step("OrderService", "Find"))
                {
                    return "ok";
                }
            }),
            BeforeRouting = builder => builder.Map("/internal/slow", branch => branch.Run(async _ =>
            {
                inside.SetResult();
                await release.Task;
            })),
        });
        var tracked = await app.GetWithTraceIdAsync("/orders", SharedTraceId);
        tracked.EnsureSuccessStatusCode();
        var traceId = tracked.Headers.GetValues(PathBaseApp.TraceIdHeader).Single();
        var stored = await app.StoredAsync(traceId);
        var spanId = stored.Spans.Single(span => span.Component == "OrderService").SpanId;

        var excluded = app.GetWithTraceIdAsync("/internal/slow", SharedTraceId);
        await inside.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var lookup = await app.GetAsync($"/flow/api/traces/{traceId}/spans/{spanId}");
        release.SetResult();
        await excluded;

        lookup.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

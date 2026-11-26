using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Shouldly;

namespace NetApiFlow.OpenTelemetry.Tests;

public class ExportedTraceIdentityTests
{
    private const string TraceIdHeader = "X-NetApiFlow-TraceId";
    private const string TraceIdTag = "netapiflow.trace_id";

    [Fact]
    public async Task ExportedSpan_OfARequestWithATraceparent_CarriesTheNativeW3CTraceIdAndTheTagWithTheIdOfTheTrace()
    {
        var exported = new List<Activity>();
        using var host = await StartHostAsync(exported);
        var w3cTraceId = ActivityTraceId.CreateRandom().ToHexString();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders");
        request.Headers.Add("traceparent", $"00-{w3cTraceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        var response = await host.GetTestClient().SendAsync(request);
        host.Services.GetRequiredService<TracerProvider>().ForceFlush();

        var traceId = response.Headers.GetValues(TraceIdHeader).Single();
        traceId.ShouldNotBe(w3cTraceId);
        var spans = exported.Where(activity => activity.GetTagItem(TraceIdTag) as string == traceId).ToList();
        spans.ShouldContain(activity => activity.OperationName == "Work.Step");
        spans.ShouldAllBe(activity => activity.TraceId.ToHexString() == w3cTraceId);
    }

    [Fact]
    public async Task ExportedSpans_OfTwoRequestsSharingATraceparent_CarryTheTagOfTheirOwnTrace()
    {
        var exported = new List<Activity>();
        using var host = await StartHostAsync(exported);
        var traceparent = $"00-{ActivityTraceId.CreateRandom().ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01";

        var traceIds = new List<string>();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/orders");
            request.Headers.Add("traceparent", traceparent);
            traceIds.Add((await host.GetTestClient().SendAsync(request)).Headers.GetValues(TraceIdHeader).Single());
        }

        host.Services.GetRequiredService<TracerProvider>().ForceFlush();

        traceIds.Distinct().Count().ShouldBe(2);
        foreach (var traceId in traceIds)
        {
            exported.Count(activity => activity.GetTagItem(TraceIdTag) as string == traceId && activity.OperationName == "Work.Step").ShouldBe(1);
        }
    }

    private static async Task<IHost> StartHostAsync(List<Activity> exported) =>
        await Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetApiFlow(options => options.EmitTraceIdHeader = true);
                    services.AddNetApiFlowOtlpExport();
                    services.ConfigureOpenTelemetryTracerProvider(builder => builder.AddInMemoryExporter(exported));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGet("/orders", () =>
                    {
                        using (Flow.Step("Work", "Step"))
                        {
                            return "ok";
                        }
                    }));
                });
            })
            .StartAsync();
}

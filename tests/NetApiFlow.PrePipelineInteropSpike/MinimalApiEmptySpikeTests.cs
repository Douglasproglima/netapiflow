using System.Net;
using Shouldly;
using Xunit.Abstractions;

namespace NetApiFlow.PrePipelineInteropSpike;

public sealed class MinimalApiEmptySpikeTests(ITestOutputHelper output)
{
    private const string EndpointMatchedEventName = "Microsoft.AspNetCore.Routing.EndpointMatched";

    [Fact]
    public async Task MinimalMediatRSample_NeverEmitsMvcFilterEvents_OnlyEndpointMatched()
    {
        using var recorder = new DiagnosticEventRecorder("Microsoft.AspNetCore");
        await using var factory = new MinimalMediatRSampleFactory();
        var client = factory.CreateClient();

        var getResponse = await client.GetAsync("/api/orders/1");
        var confirmResponse = await client.GetAsync("/api/orders/1/confirm");
        var parallelResponse = await client.GetAsync("/api/orders/parallel?ids=1&ids=2");

        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        confirmResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        parallelResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var eventNames = recorder.Events
            .Select(e => e.EventName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        output.WriteLine($"ANSWER question(3): event names observed against Minimal API across 3 requests = {string.Join(", ", eventNames)}");

        var mvcEventNames = eventNames.Where(name => name.StartsWith("Microsoft.AspNetCore.Mvc.", StringComparison.Ordinal)).ToList();
        output.WriteLine($"ANSWER question(3): Mvc filter events observed = {mvcEventNames.Count}");

        eventNames.ShouldContain(EndpointMatchedEventName);
        mvcEventNames.ShouldBeEmpty();
    }
}

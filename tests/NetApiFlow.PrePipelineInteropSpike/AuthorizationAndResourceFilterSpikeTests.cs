using System.Net;
using Shouldly;
using Xunit.Abstractions;

namespace NetApiFlow.PrePipelineInteropSpike;

public sealed class AuthorizationAndResourceFilterSpikeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AuthorizationAndResourceFilterEvents_FireWithGlobalFiltersRegistered_ReportsRealShape()
    {
        using var recorder = new DiagnosticEventRecorder("Microsoft.AspNetCore");
        await using var factory = new ControllersSampleWithAuthorizationAndResourceFiltersFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders/1");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var eventNames = recorder.Events
            .Select(e => e.EventName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        output.WriteLine($"ANSWER addendum: event names observed with global Authorization+Resource filters registered = {string.Join(", ", eventNames)}");

        var authorizationEvents = recorder.Events
            .Where(e => e.EventName.Contains("Authorization", StringComparison.Ordinal))
            .ToList();
        var resourceEvents = recorder.Events
            .Where(e => e.EventName.Contains("Resource", StringComparison.Ordinal))
            .ToList();

        output.WriteLine($"ANSWER addendum: authorizationEvents={authorizationEvents.Count} resourceEvents={resourceEvents.Count}");

        foreach (var recordedEvent in authorizationEvents.DistinctBy(e => e.EventName))
        {
            output.WriteLine($"ANSWER addendum Authorization: eventName={recordedEvent.EventName}");
            output.WriteLine($"  payloadShape={PayloadShapeDumper.Describe(recordedEvent.Payload)}");
        }

        foreach (var recordedEvent in resourceEvents.DistinctBy(e => e.EventName))
        {
            output.WriteLine($"ANSWER addendum Resource: eventName={recordedEvent.EventName}");
            output.WriteLine($"  payloadShape={PayloadShapeDumper.Describe(recordedEvent.Payload)}");
        }

        authorizationEvents.ShouldNotBeEmpty();
        resourceEvents.ShouldNotBeEmpty();
    }
}

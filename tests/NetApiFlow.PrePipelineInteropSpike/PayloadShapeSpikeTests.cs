using System.Net;
using Shouldly;
using Xunit.Abstractions;

namespace NetApiFlow.PrePipelineInteropSpike;

public sealed class PayloadShapeSpikeTests(ITestOutputHelper output)
{
    private const string EndpointMatchedEventName = "Microsoft.AspNetCore.Routing.EndpointMatched";

    [Fact]
    public async Task EndpointMatchedAndMvcFilterPayloads_ReportsRealTypeAndPropertyShape()
    {
        using var recorder = new DiagnosticEventRecorder("Microsoft.AspNetCore");
        await using var factory = new ControllersSampleFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders/1");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var endpointMatched = recorder.Events.FirstOrDefault(e => e.EventName == EndpointMatchedEventName);
        var beforeAction = recorder.Events.FirstOrDefault(e => e.EventName.Contains("BeforeAction", StringComparison.Ordinal));
        var onActionExecuting = recorder.Events.FirstOrDefault(e => e.EventName.Contains("OnActionExecuting", StringComparison.Ordinal));

        endpointMatched.ShouldNotBeNull();
        beforeAction.ShouldNotBeNull();
        onActionExecuting.ShouldNotBeNull();

        Report("ANSWER question(2) EndpointMatched", endpointMatched);
        Report("ANSWER question(2) BeforeAction", beforeAction);
        Report("ANSWER question(2) OnActionExecuting", onActionExecuting);
    }

    private void Report(string label, RecordedDiagnosticEvent recordedEvent)
    {
        output.WriteLine($"{label}: eventName={recordedEvent.EventName}");
        output.WriteLine($"  payloadShape={PayloadShapeDumper.Describe(recordedEvent.Payload)}");
    }
}

using System.Net;
using Shouldly;
using Xunit.Abstractions;

namespace NetApiFlow.PrePipelineInteropSpike;

public sealed class CorrelationSpikeTests(ITestOutputHelper output)
{
    private const string EndpointMatchedEventName = "Microsoft.AspNetCore.Routing.EndpointMatched";
    private const int ConcurrentRequestCount = 30;

    [Fact]
    public async Task Sequential_EndpointMatchedAndMvcFilters_SeeTheRequestOwnActivityTraceId()
    {
        using var recorder = new DiagnosticEventRecorder("Microsoft.AspNetCore");
        await using var factory = new ControllersSampleFactory();
        var client = factory.CreateClient();
        var traceId = TraceParentRequests.NewTraceId();

        var response = await TraceParentRequests.SendAsync(client, "/api/orders/1", traceId);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        output.WriteLine($"event names observed: {string.Join(", ", DistinctEventNames(recorder))}");

        var relevant = RelevantEvents(recorder).ToList();
        relevant.ShouldNotBeEmpty();
        foreach (var recordedEvent in relevant)
        {
            output.WriteLine($"  {recordedEvent.Describe()}");
        }

        var endpointMatchedCount = relevant.Count(e => e.EventName == EndpointMatchedEventName);
        var beforeActionCount = relevant.Count(e => e.EventName.Contains("BeforeAction", StringComparison.Ordinal));
        var onActionExecutingCount = relevant.Count(e => e.EventName.Contains("OnActionExecuting", StringComparison.Ordinal));
        var matchingTraceId = relevant.Count(e => e.ActivityTraceId == traceId);

        output.WriteLine($"ANSWER question(1) sequential: endpointMatched={endpointMatchedCount} beforeAction={beforeActionCount} " +
            $"onActionExecuting={onActionExecutingCount} matchingRequestTraceId={matchingTraceId}/{relevant.Count}");

        endpointMatchedCount.ShouldBeGreaterThan(0);
        beforeActionCount.ShouldBeGreaterThan(0);
        onActionExecutingCount.ShouldBeGreaterThan(0);
        matchingTraceId.ShouldBe(relevant.Count);
    }

    [Fact]
    public async Task Concurrent_EndpointMatchedAndMvcFilters_EachSeeTheirOwnRequestActivityTraceId()
    {
        using var recorder = new DiagnosticEventRecorder("Microsoft.AspNetCore");
        await using var factory = new ControllersSampleFactory();
        var client = factory.CreateClient();

        var traceIds = Enumerable.Range(0, ConcurrentRequestCount)
            .Select(_ => TraceParentRequests.NewTraceId())
            .ToList();

        var responses = await Task.WhenAll(
            traceIds.Select(traceId => TraceParentRequests.SendAsync(client, "/api/orders/1", traceId)));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);

        var relevant = RelevantEvents(recorder).ToList();
        var expectedTraceIds = new HashSet<string>(traceIds, StringComparer.Ordinal);

        var matched = relevant.Count(e => e.ActivityTraceId is not null && expectedTraceIds.Contains(e.ActivityTraceId));
        var mismatched = relevant.Count - matched;

        output.WriteLine($"ANSWER question(1) concurrent: requests={ConcurrentRequestCount} relevantEvents={relevant.Count} " +
            $"matchedOwnTraceId={matched} mismatched={mismatched}");

        foreach (var recordedEvent in relevant.Where(e => e.ActivityTraceId is null || !expectedTraceIds.Contains(e.ActivityTraceId)))
        {
            output.WriteLine($"  MISMATCH {recordedEvent.Describe()}");
        }

        var byTraceId = relevant
            .Where(e => e.ActivityTraceId is not null)
            .GroupBy(e => e.ActivityTraceId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var tracesWithoutAnyRelevantEvent = expectedTraceIds.Count(id => !byTraceId.ContainsKey(id));

        output.WriteLine($"  requestsWithNoRelevantEventRecorded={tracesWithoutAnyRelevantEvent}");

        mismatched.ShouldBe(0);
        tracesWithoutAnyRelevantEvent.ShouldBe(0);
    }

    private static IEnumerable<RecordedDiagnosticEvent> RelevantEvents(DiagnosticEventRecorder recorder) =>
        recorder.Events.Where(e =>
            e.EventName == EndpointMatchedEventName
            || e.EventName.Contains("BeforeAction", StringComparison.Ordinal)
            || e.EventName.Contains("OnActionExecuting", StringComparison.Ordinal));

    private static IEnumerable<string> DistinctEventNames(DiagnosticEventRecorder recorder) =>
        recorder.Events.Select(e => e.EventName).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal);
}

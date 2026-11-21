using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

public abstract class TraceIdentityTestsBase
{
    protected abstract void AddStorage(IServiceCollection services, string tablePrefix);

    private Task<TraceIdentityHost> StartHostAsync() =>
        TraceIdentityHost.StartAsync(services => AddStorage(services, $"i{Guid.NewGuid():N}_"));

    [Fact]
    public async Task Requests_SharingOneTraceparentOneAfterTheOther_AreStoredAsOneTraceEachWithoutErrors()
    {
        await using var host = await StartHostAsync();
        var traceparent = TraceIdentityHost.NewTraceparent();

        foreach (var path in new[] { "/orders", "/stock", "/orders" })
        {
            (await host.GetAsync(path, traceparent)).EnsureSuccessStatusCode();
        }

        var stored = await host.StoredTracesAsync(expectedCount: 3);

        host.NetApiFlowWarningsAndErrors().ShouldBeEmpty();
        stored.Select(trace => trace.RouteTemplate).Order().ShouldBe(["/orders", "/orders", "/stock"]);
        stored.Select(trace => trace.TraceId).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Requests_SharingOneTraceparentAtTheSameTime_GetDifferentTraceIdsAndKeepOnlyTheirOwnSteps()
    {
        await using var host = await StartHostAsync();
        var traceparent = TraceIdentityHost.NewTraceparent();

        var responses = await Task.WhenAll(host.GetAsync("/slow", traceparent), host.GetAsync("/slow", traceparent));

        var traceIds = responses.Select(response => response.Headers.TryGetValues(TraceIdentityHost.TraceIdHeader, out var values) ? values.Single() : null).ToArray();
        traceIds.ShouldAllBe(traceId => traceId != null);
        traceIds.Distinct().Count().ShouldBe(2);

        var stored = await host.StoredTracesAsync(expectedCount: 2);
        stored.Count.ShouldBe(2);
        foreach (var trace in stored)
        {
            var spans = await host.SpansOfAsync(trace.TraceId);
            spans.Count(span => span.Component == "Work").ShouldBe(1);
        }
    }

    [Fact]
    public async Task Request_WhoseTraceparentCarriesTheIdOfAStoredTrace_LeavesThatTraceAsItWas()
    {
        await using var host = await StartHostAsync();
        var first = await host.GetAsync("/orders");
        var firstTraceId = first.Headers.GetValues(TraceIdentityHost.TraceIdHeader).Single();
        await host.StoredTracesAsync(expectedCount: 1);

        (await host.GetAsync("/stock", TraceIdentityHost.NewTraceparent(firstTraceId))).EnsureSuccessStatusCode();

        var stored = await host.StoredTracesAsync(expectedCount: 2);

        host.NetApiFlowWarningsAndErrors().ShouldBeEmpty();
        stored.Select(trace => trace.RouteTemplate).Order().ShouldBe(["/orders", "/stock"]);
        stored.Single(trace => trace.TraceId == firstTraceId).RouteTemplate.ShouldBe("/orders");
    }
}

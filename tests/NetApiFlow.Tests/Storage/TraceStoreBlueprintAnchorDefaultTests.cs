using NetApiFlow.Domain;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Storage;

public class TraceStoreBlueprintAnchorDefaultTests
{
    private static readonly DateTime Anchor = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);

    private static FlowTraceDetail DetailStartedAt(DateTime startedAt) => new()
    {
        Trace = new FlowTrace
        {
            TraceId = "latest",
            Method = "GET",
            RouteTemplate = "/api/orders/{id}",
            Status = FlowStatus.Success,
            StartedAt = startedAt,
        },
        Spans = [],
    };

    [Fact]
    public async Task GetLastSuccessfulAsync_StoreWithoutTheAnchoredMember_ReturnsTheLatestWhenItStartedBeforeTheAnchor()
    {
        ITraceStore store = new StoreWithoutTheAnchoredMember(DetailStartedAt(Anchor.AddSeconds(-1)));

        var blueprint = await store.GetLastSuccessfulAsync("GET", "/api/orders/{id}", Anchor, CancellationToken.None);

        blueprint.ShouldNotBeNull().Trace.TraceId.ShouldBe("latest");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(600)]
    public async Task GetLastSuccessfulAsync_StoreWithoutTheAnchoredMember_NeverReturnsATraceThatStartedAtOrAfterTheAnchor(int secondsAfterTheAnchor)
    {
        ITraceStore store = new StoreWithoutTheAnchoredMember(DetailStartedAt(Anchor.AddSeconds(secondsAfterTheAnchor)));

        var blueprint = await store.GetLastSuccessfulAsync("GET", "/api/orders/{id}", Anchor, CancellationToken.None);

        blueprint.ShouldBeNull();
    }

    [Fact]
    public async Task GetLastSuccessfulAsync_StoreWithoutTheAnchoredMember_ReturnsNullWhenThereIsNoSuccessfulTrace()
    {
        ITraceStore store = new StoreWithoutTheAnchoredMember(null);

        var blueprint = await store.GetLastSuccessfulAsync("GET", "/api/orders/{id}", Anchor, CancellationToken.None);

        blueprint.ShouldBeNull();
    }

    [Fact]
    public async Task GetLastSuccessfulAsync_StoreWithoutTheAnchoredMember_AsksOnlyTheMemberItAlreadyImplements()
    {
        var store = new StoreWithoutTheAnchoredMember(DetailStartedAt(Anchor.AddSeconds(-1)));

        await ((ITraceStore)store).GetLastSuccessfulAsync("POST", "/api/orders/{id}", Anchor, CancellationToken.None);

        store.Asked.ShouldHaveSingleItem().ShouldBe(("POST", "/api/orders/{id}"));
    }

    private sealed class StoreWithoutTheAnchoredMember(FlowTraceDetail? latest) : ITraceStore
    {
        public List<(string Method, string RouteKey)> Asked { get; } = [];

        public ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeKey, CancellationToken cancellationToken)
        {
            Asked.Add((method, routeKey));
            return ValueTask.FromResult(latest);
        }

        public ValueTask SaveAsync(FlowTrace trace, IReadOnlyList<FlowSpan> spans, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<FlowTraceDetail?> GetAsync(string traceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<PagedResult<FlowTraceSummary>> QueryAsync(TraceQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ComponentStat>> GetComponentStatsAsync(StatsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<TraceStats> GetTraceStatsAsync(StatsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RouteStat>> GetRouteStatsAsync(StatsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ArchitectureMap> GetArchitectureMapAsync(string method, string routeKey, StatsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

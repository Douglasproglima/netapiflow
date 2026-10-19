using NetApiFlow.Routing;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Routing;

public class RouteGlobMatcherTests
{
    [Theory]
    [InlineData("/api/*", "/api/orders", true)]
    [InlineData("/api/*", "/api/orders/1", false)]
    [InlineData("/api/*/items", "/api/orders/items", true)]
    [InlineData("/api/*/items", "/api/items", false)]
    public void Matches_WithSingleSegmentWildcard_MatchesExactlyOneSegment(string pattern, string path, bool expected)
    {
        RouteGlobMatcher.Matches(pattern, path).ShouldBe(expected);
    }

    [Theory]
    [InlineData("/api/**", "/api", true)]
    [InlineData("/api/**", "/api/orders", true)]
    [InlineData("/api/**", "/api/orders/1/items", true)]
    [InlineData("/api/**", "/health", false)]
    public void Matches_WithDoubleWildcard_MatchesAnyDepth(string pattern, string path, bool expected)
    {
        RouteGlobMatcher.Matches(pattern, path).ShouldBe(expected);
    }

    [Fact]
    public void ShouldTrace_WhenExcludePathsMatchesSameRouteAsIncludePaths_ExcludeWins()
    {
        var options = new NetApiFlowOptions();
        options.IncludePaths("/api/**");
        options.ExcludePaths("/api/health");

        var policy = new TracingRoutePolicy(options);

        policy.ShouldTrace("/api/orders").ShouldBeTrue();
        policy.ShouldTrace("/api/health").ShouldBeFalse();
    }

    [Fact]
    public void ShouldTrace_ForDashboardRoute_IsAlwaysFalseEvenWithoutExplicitConfiguration()
    {
        var options = new NetApiFlowOptions();
        options.IncludePaths("/**");

        var policy = new TracingRoutePolicy(options);

        policy.ShouldTrace("/flow/api/traces").ShouldBeFalse();
    }

    [Fact]
    public void ShouldTrace_ForDashboardRoute_IsAlwaysFalseWhenDashboardMountedElsewhere()
    {
        var options = new NetApiFlowOptions { DashboardBasePath = "/diagnostics" };
        options.IncludePaths("/**");

        var policy = new TracingRoutePolicy(options);

        policy.ShouldTrace("/diagnostics/api/traces").ShouldBeFalse();
    }
}

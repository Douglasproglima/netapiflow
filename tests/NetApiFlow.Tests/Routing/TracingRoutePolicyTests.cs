using NetApiFlow.Routing;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Routing;

public class TracingRoutePolicyTests
{
    [Fact]
    public void ShouldTrace_ReadsThePatternsOnceAtConstruction()
    {
        var options = new NetApiFlowOptions();
        var subject = new TracingRoutePolicy(options);

        options.ExcludePaths("/health");

        subject.ShouldTrace("/health").ShouldBeTrue();
    }

    [Fact]
    public void Constructor_WithAPatternThatIsNull_Throws()
    {
        var options = new NetApiFlowOptions();
        options.IncludePaths(new string[] { null! });

        Should.Throw<NullReferenceException>(() => new TracingRoutePolicy(options));
    }

    [Theory]
    [InlineData("/flow/api/config", false)]
    [InlineData("/flow", false)]
    [InlineData("/openapi/v1.json", false)]
    [InlineData("/OpenApi/V1.json", false)]
    [InlineData("/orders", true)]
    public void ShouldTrace_AlwaysSkipsTheDashboardAndTheOpenApiDocument(string path, bool expected)
    {
        var subject = new TracingRoutePolicy(new NetApiFlowOptions());

        subject.ShouldTrace(path).ShouldBe(expected);
    }

    [Fact]
    public void ShouldTrace_WithAnIncludePattern_TracesOnlyWhatItMatchesAndAnExcludeStillWins()
    {
        var options = new NetApiFlowOptions();
        options.IncludePaths("/orders/**").ExcludePaths("/orders/internal/**");
        var subject = new TracingRoutePolicy(options);

        subject.ShouldTrace("/orders/7").ShouldBeTrue();
        subject.ShouldTrace("/orders/internal/7").ShouldBeFalse();
        subject.ShouldTrace("/customers/7").ShouldBeFalse();
    }
}

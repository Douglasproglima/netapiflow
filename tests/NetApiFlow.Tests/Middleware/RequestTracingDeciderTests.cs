using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Middleware;
using Shouldly;
using Xunit;

namespace NetApiFlow.Tests.Middleware;

public class RequestTracingDeciderTests
{
    private static RequestTracingDecider CreateSubject(Action<NetApiFlowOptions> configure, ListLogger? logger = null)
    {
        var options = new NetApiFlowOptions();
        configure(options);
        return new RequestTracingDecider(Options.Create(options), logger ?? new ListLogger());
    }

    private static RouteEndpoint EndpointFor(string pattern, params object[] metadata) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0, new EndpointMetadataCollection(metadata), pattern);

    [Fact]
    public void ShouldTraceFullPath_ForAPathNoPatternRejects_IsTrue()
    {
        var subject = CreateSubject(options => options.ExcludePaths("/health"));

        subject.ShouldTraceFullPath("/orders").ShouldBeTrue();
        subject.ShouldTraceFullPath("/health").ShouldBeFalse();
    }

    [Fact]
    public void ShouldTraceMatchedEndpoint_ForAnEndpointWithTheExclusionMetadata_IsFalseWhateverThePatternsSay()
    {
        var subject = CreateSubject(options => options.IncludePaths("/excluded"));
        var excluded = EndpointFor("/excluded", EndpointTracingExclusion.Instance);

        subject.ShouldTraceMatchedEndpoint(excluded, "/excluded").ShouldBeFalse();
    }

    [Fact]
    public void ShouldTraceMatchedEndpoint_ValuesThePathRelativeToThePathBase()
    {
        var subject = CreateSubject(options => options.ExcludePaths("/health"));

        subject.ShouldTraceMatchedEndpoint(EndpointFor("/health"), "/health").ShouldBeFalse();
        subject.ShouldTraceMatchedEndpoint(EndpointFor("/health"), "/api/health").ShouldBeTrue();
    }

    [Fact]
    public void ShouldTraceMatchedEndpoint_WithNoEndpoint_FollowsThePatterns()
    {
        var subject = CreateSubject(options => options.ExcludePaths("/health"));

        subject.ShouldTraceMatchedEndpoint(null, "/orders").ShouldBeTrue();
        subject.ShouldTraceMatchedEndpoint(null, "/health").ShouldBeFalse();
    }

    [Fact]
    public void WhenThePolicyCannotBeBuilt_NoRequestIsTraced_AndTheFailureIsLoggedOnce()
    {
        var logger = new ListLogger();
        var subject = CreateSubject(options => options.IncludePaths(new string[] { null! }), logger);

        var byFullPath = subject.ShouldTraceFullPath("/orders");
        var byEndpoint = subject.ShouldTraceMatchedEndpoint(EndpointFor("/orders"), "/orders");
        var again = subject.ShouldTraceFullPath("/orders");

        byFullPath.ShouldBeFalse();
        byEndpoint.ShouldBeFalse();
        again.ShouldBeFalse();
        logger.Warnings.ShouldHaveSingleItem().ShouldContain("could not decide whether to trace");
    }

    private sealed class ListLogger : ILogger<RequestTracingDecider>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}

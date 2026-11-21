using Microsoft.AspNetCore.Builder;
using NetApiFlow.Middleware;

namespace NetApiFlow;

/// <summary>Conventions that keep endpoints out of NetApiFlow tracing.</summary>
public static class NetApiFlowEndpointConventionExtensions
{
    /// <summary>
    /// Marks the endpoint, or every endpoint of a group, as never traced. It is read from the matched
    /// endpoint's metadata, so it holds under <c>UsePathBase</c>, and it wins over <c>ExcludePaths</c>,
    /// <c>IncludePaths</c> and <c>WithNetApiFlowTracking()</c> on the same endpoint. Unlike the path
    /// patterns, which are compared with the path relative to the <c>PathBase</c> for a matched endpoint
    /// and with the full path for a request that matches none, this does not depend on the path.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder: a Minimal API endpoint, a group, <c>MapControllers()</c> or <c>MapHealthChecks()</c>.</typeparam>
    /// <param name="builder">The endpoint, or the group, to keep out of tracing.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder WithoutNetApiFlowTracking<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint => endpoint.Metadata.Add(EndpointTracingExclusion.Instance));
        return builder;
    }
}

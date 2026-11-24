using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NetApiFlow.Middleware;

namespace NetApiFlow;

/// <summary>Applies automatic <see cref="ComponentType.Endpoint"/> tracking to Minimal API endpoints.</summary>
public static class NetApiFlowEndpointFilterExtensions
{
    /// <summary>
    /// Wraps the endpoint, or every endpoint the builder covers, in a tracked <see cref="ComponentType.Endpoint"/> step.
    /// The step comes from an endpoint filter, so it applies to Minimal API endpoints.
    /// </summary>
    /// <typeparam name="TBuilder">The builder type, returned as it came in so the chain keeps its type.</typeparam>
    /// <param name="builder">The endpoint, group or other convention builder to track.</param>
    /// <returns>The same builder.</returns>
    public static TBuilder WithNetApiFlowTracking<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter<TBuilder, FlowEndpointFilter>();
    }

    /// <summary>Wraps the endpoint in a tracked <see cref="ComponentType.Endpoint"/> step.</summary>
    /// <param name="builder">The endpoint to track.</param>
    /// <returns>The same builder.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static RouteHandlerBuilder WithNetApiFlowTracking(this RouteHandlerBuilder builder) =>
        builder.WithNetApiFlowTracking<RouteHandlerBuilder>();

    /// <summary>Wraps every endpoint in the route group in a tracked <see cref="ComponentType.Endpoint"/> step.</summary>
    /// <param name="builder">The group to track.</param>
    /// <returns>The same builder.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static RouteGroupBuilder WithNetApiFlowTracking(this RouteGroupBuilder builder) =>
        builder.WithNetApiFlowTracking<RouteGroupBuilder>();
}

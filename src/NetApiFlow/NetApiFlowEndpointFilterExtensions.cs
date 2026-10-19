using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NetApiFlow.Middleware;

namespace NetApiFlow;

/// <summary>Applies automatic <see cref="ComponentType.Endpoint"/> tracking to Minimal API endpoints.</summary>
public static class NetApiFlowEndpointFilterExtensions
{
    /// <summary>Wraps the endpoint in a tracked <see cref="ComponentType.Endpoint"/> step.</summary>
    public static RouteHandlerBuilder WithNetApiFlowTracking(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<FlowEndpointFilter>();

    /// <summary>Wraps every endpoint in the route group in a tracked <see cref="ComponentType.Endpoint"/> step.</summary>
    public static RouteGroupBuilder WithNetApiFlowTracking(this RouteGroupBuilder builder) =>
        builder.AddEndpointFilter<FlowEndpointFilter>();
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace NetApiFlow.Middleware;

/// <summary>Wraps a Minimal API endpoint execution in a <see cref="ComponentType.Endpoint"/> step.</summary>
internal sealed class FlowEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var endpoint = context.HttpContext.GetEndpoint();
        var component = endpoint?.DisplayName ?? "Endpoint";
        var operation = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? component;

        using var step = NetApiFlow.Step(component, operation, ComponentType.Endpoint);

        try
        {
            return await next(context);
        }
        catch (Exception ex)
        {
            step.MarkError(ex);
            throw;
        }
    }
}

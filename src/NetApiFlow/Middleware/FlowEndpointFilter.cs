using Microsoft.AspNetCore.Http;
using NetApiFlow.Routing;

namespace NetApiFlow.Middleware;

internal sealed class FlowEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var name = EndpointStepName.For(context.HttpContext.GetEndpoint());

        using var step = NetApiFlow.Step(name.Component, name.Operation, ComponentType.Endpoint);

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

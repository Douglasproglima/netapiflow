using Microsoft.AspNetCore.Http;
using NetApiFlow.Routing;

namespace NetApiFlow.Middleware;

internal sealed class FlowEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.Items.ContainsKey(FlowHttpContextItems.ImplicitEndpointStep))
        {
            return await next(context);
        }

        var name = EndpointStepName.For(context.HttpContext.GetEndpoint());

        using var step = Flow.Step(name.Component, name.Operation, ComponentType.Endpoint);

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

using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NetApiFlow.Middleware;

/// <summary>Wraps the Resource filter stage, and everything downstream of it, in a
/// <see cref="ComponentType.Service"/> step. Only registered when
/// <see cref="NetApiFlowOptions.CapturePrePipelineSteps"/> is enabled.</summary>
internal sealed class FlowResourceFilter : IAsyncResourceFilter, IOrderedFilter
{
    public int Order => int.MinValue;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var operation = (context.ActionDescriptor as ControllerActionDescriptor)?.ActionName
            ?? context.ActionDescriptor.DisplayName
            ?? "Resource";

        using var step = NetApiFlow.Step("ResourceFilters", operation, ComponentType.Service);

        try
        {
            var executed = await next();
            if (executed.Exception is not null && !executed.ExceptionHandled)
            {
                step.MarkError(executed.Exception);
            }
        }
        catch (Exception ex)
        {
            step.MarkError(ex);
            throw;
        }
    }
}

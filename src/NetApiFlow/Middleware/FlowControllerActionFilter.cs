using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NetApiFlow.Middleware;

/// <summary>Wraps every controller action execution in a <see cref="ComponentType.Controller"/> step.</summary>
internal sealed class FlowControllerActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var component = context.Controller.GetType().Name;
        var operation = (context.ActionDescriptor as ControllerActionDescriptor)?.ActionName
            ?? context.ActionDescriptor.DisplayName
            ?? "Action";

        using var step = NetApiFlow.Step(component, operation, ComponentType.Controller);

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

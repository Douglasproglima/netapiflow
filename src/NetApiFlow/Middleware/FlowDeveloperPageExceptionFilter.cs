using Microsoft.AspNetCore.Diagnostics;

namespace NetApiFlow.Middleware;

internal sealed class FlowDeveloperPageExceptionFilter : IDeveloperPageExceptionFilter
{
    public Task HandleExceptionAsync(ErrorContext errorContext, Func<ErrorContext, Task> next)
    {
        var items = errorContext.HttpContext.Items;
        if (items.ContainsKey(FlowHttpContextItems.TraceStarted))
        {
            items[FlowHttpContextItems.SwallowedException] = errorContext.Exception;
        }

        return next(errorContext);
    }
}

using Microsoft.AspNetCore.Mvc.Filters;

namespace NetApiFlow.PrePipelineInteropSpike;

internal sealed class SpikeAuthorizationFilter : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context) => Task.CompletedTask;
}

internal sealed class SpikeResourceFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next) =>
        await next();
}

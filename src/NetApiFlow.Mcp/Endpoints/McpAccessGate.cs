using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetApiFlow.Mcp.Endpoints;

internal static class McpAccessGate
{
    public static async ValueTask<object?> EnforceAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        SecurityHeaders.Apply(httpContext.Response);
        var customFilter = httpContext.RequestServices.GetService<IFlowAccessFilter>();

        if (customFilter is not null)
        {
            return await customFilter.AuthorizeAsync(httpContext)
                ? await next(context)
                : Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var environment = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        if (environment.IsDevelopment())
        {
            return await next(context);
        }

        return IsGuardedByAuthorization(httpContext.GetEndpoint()) ? await next(context) : Results.NotFound();
    }

    private static bool IsGuardedByAuthorization(Endpoint? endpoint) =>
        endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null
        && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetApiFlow.Dashboard.Endpoints;

/// <summary>
/// Shared gate for every dashboard route, API and static UI alike (ADR-0006, risco R-09): outside
/// Development, a route 404s unless <c>RequireAuthorization</c> or an
/// <see cref="IDashboardAuthorizationFilter"/> was configured. An <see cref="IDashboardAuthorizationFilter"/>,
/// when registered, is always enforced (even in Development, since the host asked for it
/// explicitly), returning <c>403</c> when it denies the request.
/// </summary>
internal static class DashboardAccessGate
{
    public static async ValueTask<object?> EnforceAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var customFilter = httpContext.RequestServices.GetService<IDashboardAuthorizationFilter>();

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

        var hasRequireAuthorization = httpContext.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is not null;
        return hasRequireAuthorization ? await next(context) : Results.NotFound();
    }
}

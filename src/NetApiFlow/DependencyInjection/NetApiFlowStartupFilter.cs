using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NetApiFlow.Middleware;

namespace NetApiFlow.DependencyInjection;

/// <summary>
/// Inserts <see cref="NetApiFlowMiddleware"/> into the pipeline automatically, so installing the
/// package and calling <c>AddNetApiFlow</c> is enough: no explicit <c>app.Use...</c> call is required.
/// </summary>
internal sealed class NetApiFlowStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.UseMiddleware<NetApiFlowMiddleware>();
            next(app);
        };
}

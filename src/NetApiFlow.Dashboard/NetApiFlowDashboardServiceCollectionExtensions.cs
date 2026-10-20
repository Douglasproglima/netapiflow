using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Dashboard.Sse;

namespace NetApiFlow.Dashboard;

/// <summary>Registers the services the dashboard needs, in addition to <c>AddNetApiFlow(...)</c>.</summary>
public static class NetApiFlowDashboardServiceCollectionExtensions
{
    /// <summary>Registers the SSE broadcaster that backs <c>GET /stream</c>. Call this before <c>Build()</c>, alongside <c>AddNetApiFlow(...)</c>.</summary>
    public static IServiceCollection AddNetApiFlowDashboard(this IServiceCollection services)
    {
        services.AddSingleton<SseBroadcaster>();
        services.AddSingleton<IFlowEventListener>(sp => sp.GetRequiredService<SseBroadcaster>());
        return services;
    }
}

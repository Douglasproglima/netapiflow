using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NetApiFlow.Dashboard.Sse;

namespace NetApiFlow.Dashboard;

/// <summary>Registers the services the dashboard needs, in addition to <c>AddNetApiFlow(...)</c>.</summary>
public static class NetApiFlowDashboardServiceCollectionExtensions
{
    /// <summary>Registers the SSE broadcaster that backs <c>GET /stream</c>. Call this before <c>Build()</c>, alongside <c>AddNetApiFlow(...)</c>.</summary>
    public static IServiceCollection AddNetApiFlowDashboard(this IServiceCollection services)
    {
        services.TryAddSingleton<SseBroadcaster>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEventListener, SseBroadcaster>(sp => sp.GetRequiredService<SseBroadcaster>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DashboardFallbackPolicyWarning>());
        return services;
    }
}

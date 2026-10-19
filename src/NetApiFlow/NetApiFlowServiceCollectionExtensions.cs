using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetApiFlow.Configuration;
using NetApiFlow.DependencyInjection;
using NetApiFlow.Events;
using NetApiFlow.Instrumentation;
using NetApiFlow.Recording;
using NetApiFlow.Storage;

namespace NetApiFlow;

/// <summary>Registers NetApiFlow tracing. See <c>UseNetApiFlowDashboard</c> in <c>NetApiFlow.Dashboard</c> for the dashboard UI.</summary>
public static class NetApiFlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers the tracing middleware, recorder, event publisher and the default in-memory trace
    /// store. Calling this more than once has no additional effect.
    /// </summary>
    public static IServiceCollection AddNetApiFlow(this IServiceCollection services, Action<NetApiFlowOptions>? configure = null)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(NetApiFlowMarkerService)))
        {
            return services;
        }

        services.AddSingleton<NetApiFlowMarkerService>();

        services.AddNetApiFlowOptions();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<FlowRecorder>();
        services.AddSingleton<FlowEventPublisher>();
        services.AddSingleton<FlowActivityListener>();
        services.AddSingleton<InMemoryTraceStore>();
        services.TryAddSingleton<ITraceStore>(sp => sp.GetRequiredService<InMemoryTraceStore>());

        services.AddSingleton<IStartupFilter, NetApiFlowStartupFilter>();
        services.AddHostedService<PersistenceWriter>();
        services.AddHostedService<RetentionService>();
        services.AddHostedService<NetApiFlowWarmupHostedService>();

        return services;
    }
}

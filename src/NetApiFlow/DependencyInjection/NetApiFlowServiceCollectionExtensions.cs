using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NetApiFlow.Configuration;
using NetApiFlow.DependencyInjection;
using NetApiFlow.Events;
using NetApiFlow.Instrumentation;
using NetApiFlow.Middleware;
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
        services.AddSingleton<InMemoryDeployAnnotationStore>();
        services.TryAddSingleton<IDeployAnnotationStore>(sp => sp.GetRequiredService<InMemoryDeployAnnotationStore>());

        services.AddSingleton<IStartupFilter, NetApiFlowStartupFilter>();
        services.Configure<MvcOptions>(options => options.Filters.Add<FlowControllerActionFilter>());
        services.AddSingleton<PersistenceWriter>();
        services.AddHostedService(sp => sp.GetRequiredService<PersistenceWriter>());
        services.AddHostedService<RetentionService>();
        services.AddHostedService<NetApiFlowWarmupHostedService>();

        var probedOptions = new NetApiFlowOptions();
        configure?.Invoke(probedOptions);
        if (probedOptions.LogCorrelation.Enabled)
        {
            services.AddSingleton<ILoggerProvider, FlowLoggerProvider>();
        }

        if (probedOptions.CapturePrePipelineSteps)
        {
            services.Configure<MvcOptions>(options => options.Filters.Add<FlowResourceFilter>());
            services.AddSingleton<FlowAuthorizationFilterListener>();
            services.AddHostedService<FlowAuthorizationFilterListenerWarmupHostedService>();
        }

        return services;
    }
}

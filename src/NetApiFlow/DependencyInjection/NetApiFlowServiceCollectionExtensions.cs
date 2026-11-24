using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Configuration;
using NetApiFlow.DependencyInjection;
using NetApiFlow.Events;
using NetApiFlow.Instrumentation;
using NetApiFlow.Middleware;
using NetApiFlow.Recording;
using NetApiFlow.Storage;

namespace NetApiFlow;

/// <summary>Registers NetApiFlow tracing. See <c>MapNetApiFlowDashboard</c> in <c>NetApiFlow.Dashboard</c> for the dashboard UI.</summary>
public static class NetApiFlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers the tracing middleware, recorder, event publisher and the default in-memory trace
    /// store. Calling it again registers nothing new, and the <paramref name="configure"/> of every call is
    /// applied, in the order of the calls.
    /// </summary>
    public static IServiceCollection AddNetApiFlow(this IServiceCollection services, Action<NetApiFlowOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(NetApiFlowMarkerService)))
        {
            AddServices(services);
        }

        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services;
    }

    private static void AddServices(IServiceCollection services)
    {
        services.AddSingleton<NetApiFlowMarkerService>();
        services.AddNetApiFlowOptions();
        services.AddSingleton<FlowRecorder>();
        services.AddSingleton<RequestTracingDecider>();
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
        services.AddHostedService(sp => sp.GetRequiredService<FlowEventPublisher>());
        services.AddSingleton(RetentionInterval.Default);
        services.AddSingleton(PersistenceRetryInterval.Default);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<RetentionService>();
        services.AddHostedService<NetApiFlowWarmupHostedService>();

        services.AddSingleton<ILoggerProvider, FlowLoggerProvider>();

        services.AddSingleton<SqlClientCommandListener>();
        services.AddHostedService<SqlClientCommandListenerWarmupHostedService>();

        services.AddSingleton<IConfigureOptions<MvcOptions>, FlowResourceFilterMvcOptionsSetup>();
        services.AddSingleton<FlowAuthorizationFilterListener>();
        services.AddHostedService<FlowAuthorizationFilterListenerWarmupHostedService>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDeveloperPageExceptionFilter, FlowDeveloperPageExceptionFilter>());
        services.AddSingleton<EndpointMatchedListener>();
        services.AddHostedService<EndpointMatchedListenerWarmupHostedService>();
    }
}

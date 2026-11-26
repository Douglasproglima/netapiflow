using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetApiFlow.Marking;

internal sealed partial class FlowConventionWarningHostedService(
    FlowConventionRegistry registry,
    ILogger<FlowConventionWarningHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            WarnAboutWhatTheShortcutMissed();
        }
        catch (Exception exception)
        {
            Log.ScanFailed(logger, exception);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void WarnAboutWhatTheShortcutMissed()
    {
        var registeredAfter = new SortedSet<string>(StringComparer.Ordinal);
        var registeredByFactory = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var descriptor in registry.Services)
        {
            if (descriptor.IsKeyedService
                || !descriptor.ServiceType.IsInterface
                || FlowConventions.IsMediatRService(descriptor.ServiceType)
                || FlowTrackedDescriptors.Contains(descriptor))
            {
                continue;
            }

            var implementationType = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
            if (implementationType is not null)
            {
                if (registry.Applied.Any(applied => applied.Matches(implementationType)))
                {
                    registeredAfter.Add(implementationType.FullName ?? implementationType.Name);
                }
            }
            else if (descriptor.ImplementationFactory is not null && registry.Applied.Any(applied => applied.Matches(descriptor.ServiceType)))
            {
                registeredByFactory.Add(descriptor.ServiceType.FullName ?? descriptor.ServiceType.Name);
            }
        }

        if (registeredAfter.Count > 0)
        {
            Log.RegisteredAfter(logger, string.Join(", ", registeredAfter));
        }

        if (registeredByFactory.Count > 0)
        {
            Log.RegisteredByFactory(logger, string.Join(", ", registeredByFactory));
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow: TrackFlowByConvention did not track these registrations, because they were registered after the call: {Types}. Call it after your own registrations.")]
        public static partial void RegisteredAfter(ILogger logger, string types);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow: TrackFlowByConvention cannot track these services, because they are registered by factory and the concrete type is unknown until the factory runs: {Services}. Register them by type, or use AddFlowTracked<TInterface, TImplementation>.")]
        public static partial void RegisteredByFactory(ILogger logger, string services);

        [LoggerMessage(Level = LogLevel.Warning, Message = "NetApiFlow: could not check which registrations TrackFlowByConvention missed.")]
        public static partial void ScanFailed(ILogger logger, Exception exception);
    }
}

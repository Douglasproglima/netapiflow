using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;

namespace NetApiFlow.DependencyInjection;

internal sealed partial class EndpointMatchedListenerWarmupHostedService(
    EndpointMatchedListener listener,
    IOptions<NetApiFlowOptions> options,
    ILogger<EndpointMatchedListenerWarmupHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (options.Value.CaptureMinimalApiEndpoints)
            {
                listener.Start();
            }
        }
        catch (Exception exception)
        {
            Log.StartFailed(logger, exception);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "NetApiFlow: could not start the endpoint listener. Minimal API endpoints will not get a step without WithNetApiFlowTracking().")]
        public static partial void StartFailed(ILogger logger, Exception exception);
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetApiFlow.Instrumentation;

namespace NetApiFlow.DependencyInjection;

internal sealed partial class EndpointMatchedListenerWarmupHostedService(
    EndpointMatchedListener listener,
    ILogger<EndpointMatchedListenerWarmupHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            listener.Start();
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
        [LoggerMessage(Level = LogLevel.Warning, Message = "NetApiFlow: could not start the endpoint listener. A request is traced or not by its full path, and Minimal API endpoints get a step only with WithNetApiFlowTracking().")]
        public static partial void StartFailed(ILogger logger, Exception exception);
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;

namespace NetApiFlow.DependencyInjection;

/// <summary>
/// Starts <see cref="FlowAuthorizationFilterListener"/> at host startup when the resolved
/// <see cref="NetApiFlowOptions.CapturePrePipelineSteps"/> is on, so the value can come from code
/// or from configuration. Left unstarted otherwise, so an app that never asked for the feature
/// pays no subscription.
/// </summary>
internal sealed partial class FlowAuthorizationFilterListenerWarmupHostedService(
    FlowAuthorizationFilterListener listener,
    IOptions<NetApiFlowOptions> options,
    ILogger<FlowAuthorizationFilterListenerWarmupHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (options.Value.CapturePrePipelineSteps)
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
        [LoggerMessage(Level = LogLevel.Warning, Message = "NetApiFlow: could not start the Authorization filter listener. Authorization filter steps will not be recorded.")]
        public static partial void StartFailed(ILogger logger, Exception exception);
    }
}

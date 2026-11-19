using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetApiFlow.Instrumentation;

namespace NetApiFlow.DependencyInjection;

/// <summary>
/// Starts <see cref="SqlClientCommandListener"/> at host startup when the resolved
/// <see cref="NetApiFlowOptions.ExternalSources"/> includes <see cref="ExternalActivitySource.SqlClient"/>.
/// Reading the resolved options is what lets the Development default and the configuration section
/// enable it, and leaving the listener unsubscribed otherwise keeps the driver from building event
/// payloads nobody reads.
/// </summary>
internal sealed partial class SqlClientCommandListenerWarmupHostedService(
    SqlClientCommandListener listener,
    IOptions<NetApiFlowOptions> options,
    ILogger<SqlClientCommandListenerWarmupHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (options.Value.ExternalSources.HasFlag(ExternalActivitySource.SqlClient))
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
        [LoggerMessage(Level = LogLevel.Warning, Message = "NetApiFlow: could not start the SqlClient command listener. SQL commands issued outside EF Core will not be recorded.")]
        public static partial void StartFailed(ILogger logger, Exception exception);
    }
}

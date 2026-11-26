using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetApiFlow.Storage;

internal sealed partial class RetentionService(
    ITraceStore store,
    IOptions<NetApiFlowOptions> options,
    RetentionInterval interval,
    ILogger<RetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval.Value);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ApplyRetentionWithoutFailingAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (IsGracefulShutdown(stoppingToken))
        {
            return;
        }
    }

    private static bool IsGracefulShutdown(CancellationToken stoppingToken) => stoppingToken.IsCancellationRequested;

    private async Task ApplyRetentionWithoutFailingAsync(CancellationToken stoppingToken)
    {
        try
        {
            var policy = new RetentionPolicy
            {
                MaxTraces = options.Value.Retention.MaxTraces,
                MaxAge = options.Value.Retention.MaxAge,
            };

            await store.ApplyRetentionAsync(policy, stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            Log.RetentionFailed(logger, ex);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "NetApiFlow: applying the trace retention failed, retention will be tried again at the next interval")]
        public static partial void RetentionFailed(ILogger logger, Exception exception);
    }
}

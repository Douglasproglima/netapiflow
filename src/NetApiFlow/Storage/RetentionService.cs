using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NetApiFlow.Storage;

/// <summary>Periodically removes traces beyond the configured retention policy.</summary>
internal sealed class RetentionService(ITraceStore store, IOptions<NetApiFlowOptions> options) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var policy = new RetentionPolicy
                {
                    MaxTraces = options.Value.Retention.MaxTraces,
                    MaxAge = options.Value.Retention.MaxAge,
                };

                await store.ApplyRetentionAsync(policy, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // PeriodicTimer.WaitForNextTickAsync throws instead of returning false when the
            // supplied token (as opposed to the timer itself) is what caused the wait to end;
            // that is the expected shape of a graceful host shutdown, not a failure.
        }
    }
}

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
}

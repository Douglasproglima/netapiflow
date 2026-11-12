using System.Diagnostics;
using Shouldly;
using Xunit.Abstractions;

namespace NetApiFlow.PrePipelineInteropSpike;

public sealed class SubscriptionCostSpikeTests(ITestOutputHelper output)
{
    private const int WarmupRequests = 20;
    private const int MeasuredRequests = 200;

    [Fact]
    public async Task DiagnosticListenerSubscriptionCost_IsMeasuredAgainstNoSubscription()
    {
        await using var factory = new ControllersSampleFactory();
        var client = factory.CreateClient();

        var withoutSubscription = await MeasureAsync(client, subscribe: false);
        var withSubscription = await MeasureAsync(client, subscribe: true);

        var overheadMs = withSubscription.TotalMilliseconds - withoutSubscription.TotalMilliseconds;
        var overheadPerRequestMicros = overheadMs * 1000 / MeasuredRequests;

        output.WriteLine($"ANSWER subscription cost: requests={MeasuredRequests} " +
            $"withoutSubscription={withoutSubscription.TotalMilliseconds:F2}ms withSubscription={withSubscription.TotalMilliseconds:F2}ms " +
            $"overhead={overheadMs:F2}ms overheadPerRequest={overheadPerRequestMicros:F1}us");

        withSubscription.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    private static async Task<TimeSpan> MeasureAsync(HttpClient client, bool subscribe)
    {
        using var recorder = subscribe ? new DiagnosticEventRecorder("Microsoft.AspNetCore") : null;

        for (var i = 0; i < WarmupRequests; i++)
        {
            await client.GetAsync("/api/orders/1");
        }

        recorder?.Clear();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < MeasuredRequests; i++)
        {
            await client.GetAsync("/api/orders/1");
        }
        stopwatch.Stop();

        return stopwatch.Elapsed;
    }
}

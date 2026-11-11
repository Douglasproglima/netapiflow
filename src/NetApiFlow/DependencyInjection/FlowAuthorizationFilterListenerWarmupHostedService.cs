using Microsoft.Extensions.Hosting;
using NetApiFlow.Instrumentation;

namespace NetApiFlow.DependencyInjection;

/// <summary>
/// Forces <see cref="FlowAuthorizationFilterListener"/> to be constructed at host startup, since
/// nothing else resolves it directly and DI singletons are otherwise lazy. Only registered when
/// <see cref="NetApiFlowOptions.CapturePrePipelineSteps"/> is enabled.
/// </summary>
internal sealed class FlowAuthorizationFilterListenerWarmupHostedService : IHostedService
{
    public FlowAuthorizationFilterListenerWarmupHostedService(FlowAuthorizationFilterListener listener)
    {
        GC.KeepAlive(listener);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

using Microsoft.Extensions.Hosting;
using NetApiFlow.Events;
using NetApiFlow.Instrumentation;

namespace NetApiFlow.DependencyInjection;

/// <summary>
/// Forces <see cref="FlowActivityListener"/> and <see cref="FlowEventPublisher"/> to be constructed at
/// host startup, since nothing else resolves them directly and DI singletons are otherwise lazy.
/// </summary>
internal sealed class NetApiFlowWarmupHostedService : IHostedService
{
    public NetApiFlowWarmupHostedService(FlowActivityListener listener, FlowEventPublisher eventPublisher)
    {
        GC.KeepAlive(listener);
        GC.KeepAlive(eventPublisher);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

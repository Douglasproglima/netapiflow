using Microsoft.Extensions.Hosting;
using NetApiFlow.Instrumentation;

namespace NetApiFlow.DependencyInjection;

/// <summary>
/// Forces <see cref="SqlClientCommandListener"/> to be constructed at host startup, since nothing
/// else resolves it directly and DI singletons are otherwise lazy. Only registered when
/// <see cref="NetApiFlowOptions.ExternalSources"/> includes <see cref="ExternalActivitySource.SqlClient"/>.
/// </summary>
internal sealed class SqlClientCommandListenerWarmupHostedService : IHostedService
{
    public SqlClientCommandListenerWarmupHostedService(SqlClientCommandListener listener)
    {
        GC.KeepAlive(listener);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

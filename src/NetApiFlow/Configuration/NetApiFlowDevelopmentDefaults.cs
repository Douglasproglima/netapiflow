using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NetApiFlow.Configuration;

/// <summary>
/// Turns on the sources the runtime and the common database drivers already emit, and the
/// trace id response header, while developing, so a fresh install shows outbound HTTP and SQL
/// and the Runner and <c>NetApiFlow.Testing</c> find their traces without configuring anything.
/// <para>Registered before configuration binding and before the caller's own lambda, and an
/// <see cref="IConfigureOptions{TOptions}"/> runs in registration order, so anything stated
/// explicitly still wins, including setting the value back to
/// <see cref="ExternalActivitySource.None"/>.</para>
/// <para><see cref="IHostEnvironment"/> is taken as optional on purpose:
/// <c>AddNetApiFlow</c> also has to work on a bare <c>ServiceCollection</c> with no host behind
/// it, where requiring it would throw at the first resolution.</para>
/// </summary>
internal sealed class NetApiFlowDevelopmentDefaults(IHostEnvironment? environment) : IConfigureOptions<NetApiFlowOptions>
{
    public void Configure(NetApiFlowOptions options)
    {
        if (environment?.IsDevelopment() != true)
        {
            return;
        }

        options.ExternalSources = ExternalActivitySource.HttpClient
            | ExternalActivitySource.Npgsql
            | ExternalActivitySource.SqlClient;
        options.EmitTraceIdHeader = true;
    }
}

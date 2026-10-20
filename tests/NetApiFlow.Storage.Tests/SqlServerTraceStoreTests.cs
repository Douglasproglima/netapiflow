using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Storage.SqlServer;

namespace NetApiFlow.Storage.Tests;

[Collection(MsSqlTestGroup.Name)]
public sealed class SqlServerTraceStoreTests(MsSqlContainerFixture fixture) : TraceStoreTestsBase
{
    protected override ITraceStore CreateStore()
    {
        var services = new ServiceCollection();
        services.AddNetApiFlowSqlServerStorage(fixture.ConnectionString, o => o.TablePrefix = $"t{Guid.NewGuid():N}_");
        return services.BuildServiceProvider().GetRequiredService<ITraceStore>();
    }
}

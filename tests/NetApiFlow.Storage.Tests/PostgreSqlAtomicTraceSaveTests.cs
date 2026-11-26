using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Storage.PostgreSql;

namespace NetApiFlow.Storage.Tests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class PostgreSqlAtomicTraceSaveTests(PostgreSqlContainerFixture fixture) : AtomicTraceSaveTestsBase
{
    protected override ITraceStore CreateStore()
    {
        var services = new ServiceCollection();
        services.AddNetApiFlowPostgreSqlStorage(fixture.ConnectionString, o => o.TablePrefix = $"t{Guid.NewGuid():N}_");
        return services.BuildServiceProvider().GetRequiredService<ITraceStore>();
    }
}

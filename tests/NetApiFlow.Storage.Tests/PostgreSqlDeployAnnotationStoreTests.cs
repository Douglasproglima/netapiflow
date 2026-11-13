using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Storage.PostgreSql;

namespace NetApiFlow.Storage.Tests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class PostgreSqlDeployAnnotationStoreTests(PostgreSqlContainerFixture fixture) : DeployAnnotationStoreTestsBase
{
    protected override IDeployAnnotationStore CreateStore()
    {
        var services = new ServiceCollection();
        services.AddNetApiFlowPostgreSqlStorage(fixture.ConnectionString, o => o.TablePrefix = $"t{Guid.NewGuid():N}_");
        return services.BuildServiceProvider().GetRequiredService<IDeployAnnotationStore>();
    }
}

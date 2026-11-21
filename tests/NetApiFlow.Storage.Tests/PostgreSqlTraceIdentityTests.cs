using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Storage.PostgreSql;

namespace NetApiFlow.Storage.Tests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class PostgreSqlTraceIdentityTests(PostgreSqlContainerFixture fixture) : TraceIdentityTestsBase
{
    protected override void AddStorage(IServiceCollection services, string tablePrefix) =>
        services.AddNetApiFlowPostgreSqlStorage(fixture.ConnectionString, options => options.TablePrefix = tablePrefix);
}

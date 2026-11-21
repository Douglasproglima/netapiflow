using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Storage.SqlServer;

namespace NetApiFlow.Storage.Tests;

[Collection(MsSqlTestGroup.Name)]
public sealed class SqlServerTraceIdentityTests(MsSqlContainerFixture fixture) : TraceIdentityTestsBase
{
    protected override void AddStorage(IServiceCollection services, string tablePrefix) =>
        services.AddNetApiFlowSqlServerStorage(fixture.ConnectionString, options => options.TablePrefix = tablePrefix);
}

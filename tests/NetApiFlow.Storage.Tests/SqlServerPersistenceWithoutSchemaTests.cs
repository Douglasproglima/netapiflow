using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Storage.SqlServer;

namespace NetApiFlow.Storage.Tests;

[Collection(MsSqlTestGroup.Name)]
public sealed class SqlServerPersistenceWithoutSchemaTests(MsSqlContainerFixture fixture) : PersistenceWithoutSchemaTestsBase
{
    protected override void AddStorage(IServiceCollection services, string tablePrefix) =>
        services.AddNetApiFlowSqlServerStorage(fixture.ConnectionString, options =>
        {
            options.TablePrefix = tablePrefix;
            options.AutoCreateSchema = false;
        });

    protected override async Task CreateSchemaAsync(string tablePrefix)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in StorageSchemaScripts.SplitIntoBatches(StorageSchemaScripts.SqlServer(tablePrefix)))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    protected override async Task<long> CountTablesAsync(string tablePrefix)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.tables WHERE name LIKE @like", connection);
        command.Parameters.AddWithValue("like", tablePrefix + "%");
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}

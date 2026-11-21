using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Storage.PostgreSql;
using Npgsql;

namespace NetApiFlow.Storage.Tests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class PostgreSqlPersistenceWithoutSchemaTests(PostgreSqlContainerFixture fixture) : PersistenceWithoutSchemaTestsBase
{
    protected override void AddStorage(IServiceCollection services, string tablePrefix) =>
        services.AddNetApiFlowPostgreSqlStorage(fixture.ConnectionString, options =>
        {
            options.TablePrefix = tablePrefix;
            options.AutoCreateSchema = false;
        });

    protected override async Task CreateSchemaAsync(string tablePrefix)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(StorageSchemaScripts.PostgreSql(tablePrefix), connection);
        await command.ExecuteNonQueryAsync();
    }

    protected override async Task<long> CountTablesAsync(string tablePrefix)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name LIKE @like", connection);
        command.Parameters.AddWithValue("like", tablePrefix + "%");
        return (long)(await command.ExecuteScalarAsync())!;
    }
}

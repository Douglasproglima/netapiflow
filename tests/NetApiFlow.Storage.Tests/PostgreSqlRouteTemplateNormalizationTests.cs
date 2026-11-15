using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Storage.PostgreSql;
using Npgsql;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class PostgreSqlRouteTemplateNormalizationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task EnsureSchemaAsync_FindsARowInsertedOutsideTheCanonicalShape_NormalizesItOnTheNextStoreStartup()
    {
        var tablePrefix = $"t{Guid.NewGuid():N}_";
        var tracesTable = $"{tablePrefix}traces";

        var firstStore = BuildStore(tablePrefix);
        await firstStore.GetAsync("does-not-exist", CancellationToken.None);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insertCommand = new NpgsqlCommand(
                $"""
                INSERT INTO {tracesTable} (trace_id, method, route_template, status, started_at, span_count, truncated, tags)
                VALUES (@traceId, 'GET', @routeTemplate, 'Success', NOW(), 0, false, @tags::jsonb)
                """,
                connection);
            insertCommand.Parameters.AddWithValue("traceId", "non-canonical-1");
            insertCommand.Parameters.AddWithValue("routeTemplate", "api/x");
            insertCommand.Parameters.AddWithValue("tags", "{}");
            await insertCommand.ExecuteNonQueryAsync();
        }

        var secondStore = BuildStore(tablePrefix);
        await secondStore.GetAsync("does-not-exist", CancellationToken.None);

        await using var verifyConnection = new NpgsqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var selectCommand = new NpgsqlCommand(
            $"SELECT route_template FROM {tracesTable} WHERE trace_id = @traceId",
            verifyConnection);
        selectCommand.Parameters.AddWithValue("traceId", "non-canonical-1");
        var routeTemplate = (string?)await selectCommand.ExecuteScalarAsync();

        routeTemplate.ShouldBe("/api/x");
    }

    private ITraceStore BuildStore(string tablePrefix)
    {
        var services = new ServiceCollection();
        services.AddNetApiFlowPostgreSqlStorage(fixture.ConnectionString, o => o.TablePrefix = tablePrefix);
        return services.BuildServiceProvider().GetRequiredService<ITraceStore>();
    }
}

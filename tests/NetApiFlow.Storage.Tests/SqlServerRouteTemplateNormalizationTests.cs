using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Storage.SqlServer;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

[Collection(MsSqlTestGroup.Name)]
public sealed class SqlServerRouteTemplateNormalizationTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task EnsureSchemaAsync_FindsARowInsertedOutsideTheCanonicalShape_NormalizesItOnTheNextStoreStartup()
    {
        var tablePrefix = $"t{Guid.NewGuid():N}_";
        var tracesTable = $"dbo.{tablePrefix}traces";

        var firstStore = BuildStore(tablePrefix);
        await firstStore.GetAsync("does-not-exist", CancellationToken.None);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insertCommand = new SqlCommand(
                $"""
                INSERT INTO {tracesTable} (trace_id, method, route_template, status, started_at, span_count, truncated, tags)
                VALUES (@traceId, 'GET', @routeTemplate, 'Success', SYSDATETIMEOFFSET(), 0, 0, @tags)
                """,
                connection);
            insertCommand.Parameters.AddWithValue("traceId", "non-canonical-1");
            insertCommand.Parameters.AddWithValue("routeTemplate", "api/x");
            insertCommand.Parameters.AddWithValue("tags", "{}");
            await insertCommand.ExecuteNonQueryAsync();
        }

        var secondStore = BuildStore(tablePrefix);
        await secondStore.GetAsync("does-not-exist", CancellationToken.None);

        await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var selectCommand = new SqlCommand(
            $"SELECT route_template FROM {tracesTable} WHERE trace_id = @traceId",
            verifyConnection);
        selectCommand.Parameters.AddWithValue("traceId", "non-canonical-1");
        var routeTemplate = (string?)await selectCommand.ExecuteScalarAsync();

        routeTemplate.ShouldBe("/api/x");
    }

    [Fact]
    public async Task EnsureSchemaAsync_FindsARowWithoutARouteKey_BackfillsItOnTheNextStoreStartup()
    {
        var tablePrefix = $"t{Guid.NewGuid():N}_";
        var tracesTable = $"dbo.{tablePrefix}traces";

        var firstStore = BuildStore(tablePrefix);
        await firstStore.GetAsync("does-not-exist", CancellationToken.None);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insertCommand = new SqlCommand(
                $"""
                INSERT INTO {tracesTable} (trace_id, method, route_template, status, started_at, span_count, truncated, tags)
                VALUES (@traceId, 'GET', @routeTemplate, 'Success', SYSDATETIMEOFFSET(), 0, 0, @tags)
                """,
                connection);
            insertCommand.Parameters.AddWithValue("traceId", "pre-route-key-1");
            insertCommand.Parameters.AddWithValue("routeTemplate", "/api/orders/{id:int}");
            insertCommand.Parameters.AddWithValue("tags", "{}");
            await insertCommand.ExecuteNonQueryAsync();
        }

        var secondStore = BuildStore(tablePrefix);
        await secondStore.GetAsync("does-not-exist", CancellationToken.None);

        await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var selectCommand = new SqlCommand(
            $"SELECT route_key FROM {tracesTable} WHERE trace_id = @traceId",
            verifyConnection);
        selectCommand.Parameters.AddWithValue("traceId", "pre-route-key-1");
        var routeKey = (string?)await selectCommand.ExecuteScalarAsync();

        routeKey.ShouldBe("/api/orders/{id}");
    }

    private ITraceStore BuildStore(string tablePrefix)
    {
        var services = new ServiceCollection();
        services.AddNetApiFlowSqlServerStorage(fixture.ConnectionString, o => o.TablePrefix = tablePrefix);
        return services.BuildServiceProvider().GetRequiredService<ITraceStore>();
    }
}

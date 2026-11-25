using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Domain;
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

    [Fact]
    public async Task EnsureSchemaAsync_FindsARowWithoutARouteKey_BackfillsItOnTheNextStoreStartup()
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
            insertCommand.Parameters.AddWithValue("traceId", "pre-route-key-1");
            insertCommand.Parameters.AddWithValue("routeTemplate", "/api/orders/{id:int}");
            insertCommand.Parameters.AddWithValue("tags", "{}");
            await insertCommand.ExecuteNonQueryAsync();
        }

        var secondStore = BuildStore(tablePrefix);
        await secondStore.GetAsync("does-not-exist", CancellationToken.None);

        await using var verifyConnection = new NpgsqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var selectCommand = new NpgsqlCommand(
            $"SELECT route_key FROM {tracesTable} WHERE trace_id = @traceId",
            verifyConnection);
        selectCommand.Parameters.AddWithValue("traceId", "pre-route-key-1");
        var routeKey = (string?)await selectCommand.ExecuteScalarAsync();

        routeKey.ShouldBe("/api/orders/{id}");
    }

    [Theory]
    [InlineData("/api/orders/{id}")]
    [InlineData("/api/orders/{id:int}")]
    [InlineData("api/orders/{id}")]
    public async Task QueryAsync_FilteredByARouteWithOrWithoutTheConstraint_FindsTheTraceSavedWithTheConstraint(string route)
    {
        var store = BuildStore($"t{Guid.NewGuid():N}_");
        await store.SaveAsync(CreateTrace("constrained", "/api/orders/{id:int}"), [], CancellationToken.None);
        await store.SaveAsync(CreateTrace("other", "/api/customers/{id:int}"), [], CancellationToken.None);

        var result = await store.QueryAsync(new TraceQuery { RouteTemplate = route }, CancellationToken.None);

        result.Items.Select(item => item.TraceId).ShouldBe(["constrained"]);
    }

    private static FlowTrace CreateTrace(string traceId, string routeTemplate) => new()
    {
        TraceId = traceId,
        Method = "GET",
        RouteTemplate = routeTemplate,
        Status = FlowStatus.Success,
        StartedAt = DateTime.UtcNow,
    };

    private ITraceStore BuildStore(string tablePrefix)
    {
        var services = new ServiceCollection();
        services.AddNetApiFlowPostgreSqlStorage(fixture.ConnectionString, o => o.TablePrefix = tablePrefix);
        return services.BuildServiceProvider().GetRequiredService<ITraceStore>();
    }
}

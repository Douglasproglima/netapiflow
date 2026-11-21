using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetApiFlow.Storage.PostgreSql;
using Npgsql;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class PostgreSqlSchemaScriptTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task Store_WithoutAutoCreateSchemaOnADatabaseBuiltOnlyByTheSchemaScript_SavesQueriesAndMapsTheArchitecture()
    {
        var tablePrefix = NewPrefix("s");
        await RunAsync(StorageSchemaScripts.PostgreSql(tablePrefix));
        var stores = BuildStores(tablePrefix, autoCreateSchema: false);

        await stores.Traces.SaveAsync(
            SchemaScriptTestData.Trace("script-built-1", "/api/orders/{id:int}"),
            [SchemaScriptTestData.Span("script-built-1", "span-1")],
            CancellationToken.None);

        var saved = await stores.Traces.GetAsync("script-built-1", CancellationToken.None);
        var page = await stores.Traces.QueryAsync(new TraceQuery(), CancellationToken.None);
        var map = await stores.Traces.GetArchitectureMapAsync("GET", "/api/orders/{id}", new StatsQuery(), CancellationToken.None);

        saved!.Trace.RouteTemplate.ShouldBe("/api/orders/{id:int}");
        page.Items.Select(trace => trace.TraceId).ShouldBe(["script-built-1"]);
        map.TotalExecutions.ShouldBe(1);
        map.Nodes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SchemaScript_BuildsTheSameCatalogAsTheAutoCreatedSchema()
    {
        var scriptPrefix = NewPrefix("s");
        var autoPrefix = NewPrefix("a");
        await RunAsync(StorageSchemaScripts.PostgreSql(scriptPrefix));
        var auto = BuildStores(autoPrefix, autoCreateSchema: true);
        await auto.Traces.GetAsync("warm-up", CancellationToken.None);
        await auto.Deploys.QueryAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, CancellationToken.None);

        var scriptCatalog = await ReadCatalogAsync(scriptPrefix);
        var autoCatalog = await ReadCatalogAsync(autoPrefix);

        scriptCatalog.ShouldNotBeEmpty();
        scriptCatalog.ShouldBe(autoCatalog);
    }

    [Fact]
    public async Task Store_WithoutAutoCreateSchemaOnAnEmptyDatabase_CreatesNoTableAndLogsWhatIsMissing()
    {
        var tablePrefix = NewPrefix("e");
        var stores = BuildStores(tablePrefix, autoCreateSchema: false);

        await Should.ThrowAsync<PostgresException>(async () => await stores.Traces.GetAsync("x", CancellationToken.None));

        (await CountTablesAsync(tablePrefix)).ShouldBe(0);
        stores.Logs.Lines.ShouldContain(line =>
            line.Level == LogLevel.Error
            && line.Message.Contains($"{tablePrefix}traces", StringComparison.Ordinal)
            && line.Message.Contains("AutoCreateSchema", StringComparison.Ordinal)
            && line.Message.Contains("schema.sql", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Store_WithoutAutoCreateSchemaOnATableWithoutRouteKey_ChangesNothingAndLogsTheMissingColumn()
    {
        var tablePrefix = NewPrefix("o");
        await CreateTableWithoutRouteKeyAsync(tablePrefix);
        await InsertRowWithoutRouteKeyAsync(tablePrefix, "old-1", "api/x");
        var stores = BuildStores(tablePrefix, autoCreateSchema: false);

        (await stores.Traces.GetAsync("not-there", CancellationToken.None)).ShouldBeNull();

        (await CountRouteKeyColumnsAsync(tablePrefix)).ShouldBe(0);
        (await ReadRouteTemplateAsync(tablePrefix, "old-1")).ShouldBe("api/x");
        stores.Logs.Lines.ShouldContain(line =>
            line.Level == LogLevel.Error && line.Message.Contains("route_key", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RouteTemplates))]
    public async Task SchemaScript_OnATableWithoutRouteKey_FillsTheKeyLikeTheNormalizer(string routeTemplate)
    {
        var tablePrefix = NewPrefix("u");
        await CreateTableWithoutRouteKeyAsync(tablePrefix);
        await InsertRowWithoutRouteKeyAsync(tablePrefix, "old-1", routeTemplate);

        await RunAsync(StorageSchemaScripts.PostgreSql(tablePrefix));

        (await ReadRouteKeyAsync(tablePrefix, "old-1")).ShouldBe(SchemaScriptTestData.ExpectedRouteKey(routeTemplate));
    }

    [Fact]
    public async Task SchemaScript_RunTwice_ChangesNeitherTheCatalogNorTheKeys()
    {
        var tablePrefix = NewPrefix("r");
        await CreateTableWithoutRouteKeyAsync(tablePrefix);
        await InsertRowWithoutRouteKeyAsync(tablePrefix, "old-1", "/api/orders/{id:int}");
        await RunAsync(StorageSchemaScripts.PostgreSql(tablePrefix));
        var catalogAfterFirstRun = await ReadCatalogAsync(tablePrefix);
        var keyAfterFirstRun = await ReadRouteKeyAsync(tablePrefix, "old-1");

        await RunAsync(StorageSchemaScripts.PostgreSql(tablePrefix));

        (await ReadCatalogAsync(tablePrefix)).ShouldBe(catalogAfterFirstRun);
        (await ReadRouteKeyAsync(tablePrefix, "old-1")).ShouldBe(keyAfterFirstRun);
    }

    public static IEnumerable<object[]> RouteTemplates() => SchemaScriptTestData.RouteTemplates();

    private static string NewPrefix(string letter) => $"{letter}{Guid.NewGuid():N}_";

    private sealed record Stores(ITraceStore Traces, IDeployAnnotationStore Deploys, CapturingLoggerProvider Logs);

    private Stores BuildStores(string tablePrefix, bool autoCreateSchema)
    {
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddNetApiFlowPostgreSqlStorage(fixture.ConnectionString, options =>
        {
            options.TablePrefix = tablePrefix;
            options.AutoCreateSchema = autoCreateSchema;
        });
        var provider = services.BuildServiceProvider();
        return new Stores(provider.GetRequiredService<ITraceStore>(), provider.GetRequiredService<IDeployAnnotationStore>(), logs);
    }

    private async Task RunAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task CreateTableWithoutRouteKeyAsync(string tablePrefix) =>
        await RunAsync($"""
            CREATE TABLE {tablePrefix}traces (
                trace_id varchar(64) PRIMARY KEY,
                method varchar(16) NOT NULL,
                route_template text NOT NULL,
                http_status int NULL,
                status varchar(16) NOT NULL,
                started_at timestamptz NOT NULL,
                ended_at timestamptz NULL,
                duration_micros bigint NULL,
                span_count int NOT NULL,
                truncated boolean NOT NULL,
                error_message text NULL,
                tags jsonb NOT NULL
            )
            """);

    private async Task InsertRowWithoutRouteKeyAsync(string tablePrefix, string traceId, string routeTemplate)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO {tablePrefix}traces (trace_id, method, route_template, status, started_at, span_count, truncated, tags)
            VALUES (@traceId, 'GET', @routeTemplate, 'Success', NOW(), 0, false, @tags::jsonb)
            """,
            connection);
        command.Parameters.AddWithValue("traceId", traceId);
        command.Parameters.AddWithValue("routeTemplate", routeTemplate);
        command.Parameters.AddWithValue("tags", "{}");
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> ReadRouteKeyAsync(string tablePrefix, string traceId) =>
        await ScalarAsync<string>($"SELECT route_key FROM {tablePrefix}traces WHERE trace_id = @id", ("id", traceId));

    private async Task<string?> ReadRouteTemplateAsync(string tablePrefix, string traceId) =>
        await ScalarAsync<string>($"SELECT route_template FROM {tablePrefix}traces WHERE trace_id = @id", ("id", traceId));

    private async Task<long> CountTablesAsync(string tablePrefix) =>
        await ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name LIKE @like",
            ("like", tablePrefix + "%"));

    private async Task<long> CountRouteKeyColumnsAsync(string tablePrefix) =>
        await ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @table AND column_name = 'route_key'",
            ("table", tablePrefix + "traces"));

    private async Task<T?> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    private async Task<IReadOnlyList<string>> ReadCatalogAsync(string tablePrefix)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var lines = new List<string>();
        var like = tablePrefix + "%";

        await using (var columns = new NpgsqlCommand(
            """
            SELECT 'column ' || replace(table_name, @prefix, '') || '.' || column_name || ' ' || data_type
                || ' nullable=' || is_nullable || ' default=' || coalesce(column_default, '')
            FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name LIKE @like
            ORDER BY replace(table_name, @prefix, ''), column_name
            """,
            connection))
        {
            columns.Parameters.AddWithValue("prefix", tablePrefix);
            columns.Parameters.AddWithValue("like", like);
            await using var reader = await columns.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lines.Add(reader.GetString(0));
            }
        }

        await using (var indexes = new NpgsqlCommand(
            """
            SELECT 'index ' || replace(indexname, @prefix, '') || ' ' || replace(indexdef, @prefix, '')
            FROM pg_indexes
            WHERE schemaname = current_schema() AND tablename LIKE @like
            ORDER BY replace(indexname, @prefix, '')
            """,
            connection))
        {
            indexes.Parameters.AddWithValue("prefix", tablePrefix);
            indexes.Parameters.AddWithValue("like", like);
            await using var reader = await indexes.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lines.Add(reader.GetString(0));
            }
        }

        return lines;
    }
}

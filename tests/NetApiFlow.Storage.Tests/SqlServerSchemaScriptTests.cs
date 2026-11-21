using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetApiFlow.Storage.SqlServer;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

[Collection(MsSqlTestGroup.Name)]
public sealed class SqlServerSchemaScriptTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task Store_WithoutAutoCreateSchemaOnADatabaseBuiltOnlyByTheSchemaScript_SavesQueriesAndMapsTheArchitecture()
    {
        var tablePrefix = NewPrefix("s");
        await RunScriptAsync(StorageSchemaScripts.SqlServer(tablePrefix));
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
        await RunScriptAsync(StorageSchemaScripts.SqlServer(scriptPrefix));
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

        await Should.ThrowAsync<SqlException>(async () => await stores.Traces.GetAsync("x", CancellationToken.None));

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

        await RunScriptAsync(StorageSchemaScripts.SqlServer(tablePrefix));

        (await ReadRouteKeyAsync(tablePrefix, "old-1")).ShouldBe(SchemaScriptTestData.ExpectedRouteKey(routeTemplate));
    }

    [Fact]
    public async Task SchemaScript_RunTwice_ChangesNeitherTheCatalogNorTheKeys()
    {
        var tablePrefix = NewPrefix("r");
        await CreateTableWithoutRouteKeyAsync(tablePrefix);
        await InsertRowWithoutRouteKeyAsync(tablePrefix, "old-1", "/api/orders/{id:int}");
        await RunScriptAsync(StorageSchemaScripts.SqlServer(tablePrefix));
        var catalogAfterFirstRun = await ReadCatalogAsync(tablePrefix);
        var keyAfterFirstRun = await ReadRouteKeyAsync(tablePrefix, "old-1");

        await RunScriptAsync(StorageSchemaScripts.SqlServer(tablePrefix));

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
        services.AddNetApiFlowSqlServerStorage(fixture.ConnectionString, options =>
        {
            options.TablePrefix = tablePrefix;
            options.AutoCreateSchema = autoCreateSchema;
        });
        var provider = services.BuildServiceProvider();
        return new Stores(provider.GetRequiredService<ITraceStore>(), provider.GetRequiredService<IDeployAnnotationStore>(), logs);
    }

    private async Task RunScriptAsync(string script)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in StorageSchemaScripts.SplitIntoBatches(script))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task RunAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task CreateTableWithoutRouteKeyAsync(string tablePrefix) =>
        await RunAsync($"""
            CREATE TABLE dbo.{tablePrefix}traces (
                trace_id varchar(64) NOT NULL PRIMARY KEY,
                method varchar(16) NOT NULL,
                route_template nvarchar(400) NOT NULL,
                http_status int NULL,
                status varchar(16) NOT NULL,
                started_at datetimeoffset NOT NULL,
                ended_at datetimeoffset NULL,
                duration_micros bigint NULL,
                span_count int NOT NULL,
                truncated bit NOT NULL,
                error_message nvarchar(max) NULL,
                tags nvarchar(max) NOT NULL
            )
            """);

    private async Task InsertRowWithoutRouteKeyAsync(string tablePrefix, string traceId, string routeTemplate) =>
        await RunAsync(
            $"""
            INSERT INTO dbo.{tablePrefix}traces (trace_id, method, route_template, status, started_at, span_count, truncated, tags)
            VALUES (@traceId, 'GET', @routeTemplate, 'Success', SYSDATETIMEOFFSET(), 0, 0, @tags)
            """,
            ("traceId", traceId),
            ("routeTemplate", routeTemplate),
            ("tags", "{}"));

    private async Task<string?> ReadRouteKeyAsync(string tablePrefix, string traceId) =>
        await ScalarAsync<string>($"SELECT route_key FROM dbo.{tablePrefix}traces WHERE trace_id = @id", ("id", traceId));

    private async Task<string?> ReadRouteTemplateAsync(string tablePrefix, string traceId) =>
        await ScalarAsync<string>($"SELECT route_template FROM dbo.{tablePrefix}traces WHERE trace_id = @id", ("id", traceId));

    private async Task<int> CountTablesAsync(string tablePrefix) =>
        await ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name LIKE @like", ("like", tablePrefix + "%"));

    private async Task<int> CountRouteKeyColumnsAsync(string tablePrefix) =>
        await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@table) AND name = 'route_key'",
            ("table", $"dbo.{tablePrefix}traces"));

    private async Task<T?> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    private async Task<IReadOnlyList<string>> ReadCatalogAsync(string tablePrefix)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var lines = new List<string>();
        var like = tablePrefix + "%";

        await ReadLinesAsync(
            connection,
            lines,
            """
            SELECT 'column ' + REPLACE(t.name COLLATE DATABASE_DEFAULT, @prefix, '') + '.' + c.name COLLATE DATABASE_DEFAULT + ' ' + ty.name COLLATE DATABASE_DEFAULT
                + '(' + CAST(c.max_length AS varchar(10)) + ') nullable=' + CAST(c.is_nullable AS varchar(1))
                + ' default=' + COALESCE(d.definition COLLATE DATABASE_DEFAULT, '')
            FROM sys.tables t
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints d ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
            WHERE t.name LIKE @like
            ORDER BY REPLACE(t.name COLLATE DATABASE_DEFAULT, @prefix, ''), c.name
            """,
            tablePrefix,
            like);

        await ReadLinesAsync(
            connection,
            lines,
            """
            SELECT 'index ' + CASE WHEN i.is_primary_key = 1 THEN 'primary-key' ELSE REPLACE(i.name COLLATE DATABASE_DEFAULT, @prefix, '') END
                + ' on ' + REPLACE(t.name COLLATE DATABASE_DEFAULT, @prefix, '') + ' unique=' + CAST(i.is_unique AS varchar(1))
                + ' columns=' + (
                    SELECT STRING_AGG(c.name COLLATE DATABASE_DEFAULT, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
                    FROM sys.index_columns ic
                    INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                    WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0)
            FROM sys.indexes i
            INNER JOIN sys.tables t ON t.object_id = i.object_id
            WHERE t.name LIKE @like AND i.type > 0
            ORDER BY REPLACE(t.name COLLATE DATABASE_DEFAULT, @prefix, ''), CASE WHEN i.is_primary_key = 1 THEN '' ELSE REPLACE(i.name COLLATE DATABASE_DEFAULT, @prefix, '') END
            """,
            tablePrefix,
            like);

        await ReadLinesAsync(
            connection,
            lines,
            """
            SELECT 'foreign-key ' + REPLACE(f.name COLLATE DATABASE_DEFAULT, @prefix, '') + ' on ' + REPLACE(t.name COLLATE DATABASE_DEFAULT, @prefix, '')
                + ' delete=' + f.delete_referential_action_desc COLLATE DATABASE_DEFAULT
            FROM sys.foreign_keys f
            INNER JOIN sys.tables t ON t.object_id = f.parent_object_id
            WHERE t.name LIKE @like
            ORDER BY REPLACE(f.name COLLATE DATABASE_DEFAULT, @prefix, '')
            """,
            tablePrefix,
            like);

        return lines;
    }

    private static async Task ReadLinesAsync(SqlConnection connection, List<string> lines, string sql, string prefix, string like)
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("prefix", prefix);
        command.Parameters.AddWithValue("like", like);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }
    }
}

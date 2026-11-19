using NetApiFlow.Routing;
using Npgsql;

namespace NetApiFlow.Storage.PostgreSql;

/// <summary>Creates the schema on first use, equivalent to <c>schema.sql</c>.</summary>
internal static class PostgreSqlSchemaInitializer
{
    public static async Task EnsureCreatedAsync(NpgsqlConnection connection, string tracesTable, string spansTable, CancellationToken cancellationToken)
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {tracesTable} (
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
            );

            CREATE INDEX IF NOT EXISTS ix_{tracesTable}_started_at ON {tracesTable} (started_at);
            CREATE INDEX IF NOT EXISTS ix_{tracesTable}_status ON {tracesTable} (status);
            CREATE INDEX IF NOT EXISTS ix_{tracesTable}_method_route ON {tracesTable} (method, route_template);
            CREATE INDEX IF NOT EXISTS ix_{tracesTable}_duration_micros ON {tracesTable} (duration_micros);

            ALTER TABLE {tracesTable} ADD COLUMN IF NOT EXISTS route_key text NOT NULL DEFAULT '';
            CREATE INDEX IF NOT EXISTS ix_{tracesTable}_method_route_key ON {tracesTable} (method, route_key);

            CREATE TABLE IF NOT EXISTS {spansTable} (
                trace_id varchar(64) NOT NULL REFERENCES {tracesTable} (trace_id) ON DELETE CASCADE,
                span_id varchar(64) NOT NULL,
                parent_span_id varchar(64) NULL,
                sequence int NOT NULL,
                component text NOT NULL,
                operation text NOT NULL,
                type varchar(32) NOT NULL,
                started_at timestamptz NOT NULL,
                ended_at timestamptz NULL,
                duration_micros bigint NULL,
                status varchar(16) NOT NULL,
                error_type text NULL,
                error_message text NULL,
                is_error_origin boolean NOT NULL,
                tags jsonb NOT NULL,
                logs jsonb NOT NULL,
                PRIMARY KEY (trace_id, span_id)
            );

            CREATE INDEX IF NOT EXISTS ix_{spansTable}_trace_id ON {spansTable} (trace_id);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Brings every already-stored row's <c>route_template</c> and <c>route_key</c> to
    /// <see cref="RouteTemplateNormalizer"/>'s canonical shape, idempotent: once every row is
    /// canonical, the distinct set to check next time is unaffected and nothing is rewritten.
    /// </summary>
    public static async Task NormalizeRoutesAsync(NpgsqlConnection connection, string tracesTable, CancellationToken cancellationToken)
    {
        var distinctRouteTemplates = new List<string>();
        await using (var selectCommand = new NpgsqlCommand($"SELECT DISTINCT route_template FROM {tracesTable}", connection))
        await using (var reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                distinctRouteTemplates.Add(reader.GetString(0));
            }
        }

        foreach (var routeTemplate in distinctRouteTemplates)
        {
            var normalized = RouteTemplateNormalizer.Normalize(routeTemplate);
            if (normalized != routeTemplate)
            {
                await using var updateTemplateCommand = new NpgsqlCommand(
                    $"UPDATE {tracesTable} SET route_template = @normalized WHERE route_template = @original",
                    connection);
                updateTemplateCommand.Parameters.AddWithValue("normalized", normalized);
                updateTemplateCommand.Parameters.AddWithValue("original", routeTemplate);
                await updateTemplateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var routeKey = RouteTemplateNormalizer.NormalizeForComparison(normalized);
            await using var updateKeyCommand = new NpgsqlCommand(
                $"UPDATE {tracesTable} SET route_key = @key WHERE route_template = @template AND route_key <> @key",
                connection);
            updateKeyCommand.Parameters.AddWithValue("key", routeKey);
            updateKeyCommand.Parameters.AddWithValue("template", normalized);
            await updateKeyCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public static async Task EnsureDeployAnnotationsCreatedAsync(NpgsqlConnection connection, string deployAnnotationsTable, CancellationToken cancellationToken)
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {deployAnnotationsTable} (
                id varchar(64) PRIMARY KEY,
                timestamp timestamptz NOT NULL,
                label text NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_{deployAnnotationsTable}_timestamp ON {deployAnnotationsTable} (timestamp);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

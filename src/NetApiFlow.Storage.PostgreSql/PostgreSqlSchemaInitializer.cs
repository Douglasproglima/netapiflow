using Npgsql;

namespace NetApiFlow.Storage.PostgreSql;

/// <summary>Creates the schema (tarefa 6.1) on first use, equivalent to <c>schema.sql</c>.</summary>
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
                PRIMARY KEY (trace_id, span_id)
            );

            CREATE INDEX IF NOT EXISTS ix_{spansTable}_trace_id ON {spansTable} (trace_id);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

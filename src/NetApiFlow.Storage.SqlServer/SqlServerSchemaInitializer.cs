using Microsoft.Data.SqlClient;

namespace NetApiFlow.Storage.SqlServer;

/// <summary>Creates the schema (tarefa 6.1) on first use, equivalent to <c>schema.sql</c>.</summary>
internal static class SqlServerSchemaInitializer
{
    public static async Task EnsureCreatedAsync(SqlConnection connection, string tracesTable, string spansTable, CancellationToken cancellationToken)
    {
        var sql = $"""
            IF OBJECT_ID('{tracesTable}', 'U') IS NULL
            BEGIN
                CREATE TABLE {tracesTable} (
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
                );

                CREATE INDEX ix_{SafeIndexName(tracesTable)}_started_at ON {tracesTable} (started_at);
                CREATE INDEX ix_{SafeIndexName(tracesTable)}_status ON {tracesTable} (status);
                CREATE INDEX ix_{SafeIndexName(tracesTable)}_method_route ON {tracesTable} (method, route_template);
                CREATE INDEX ix_{SafeIndexName(tracesTable)}_duration_micros ON {tracesTable} (duration_micros);
            END;

            IF OBJECT_ID('{spansTable}', 'U') IS NULL
            BEGIN
                CREATE TABLE {spansTable} (
                    trace_id varchar(64) NOT NULL,
                    span_id varchar(64) NOT NULL,
                    parent_span_id varchar(64) NULL,
                    sequence int NOT NULL,
                    component nvarchar(max) NOT NULL,
                    operation nvarchar(max) NOT NULL,
                    type varchar(32) NOT NULL,
                    started_at datetimeoffset NOT NULL,
                    ended_at datetimeoffset NULL,
                    duration_micros bigint NULL,
                    status varchar(16) NOT NULL,
                    error_type nvarchar(max) NULL,
                    error_message nvarchar(max) NULL,
                    is_error_origin bit NOT NULL,
                    tags nvarchar(max) NOT NULL,
                    CONSTRAINT pk_{SafeIndexName(spansTable)} PRIMARY KEY (trace_id, span_id),
                    CONSTRAINT fk_{SafeIndexName(spansTable)}_trace FOREIGN KEY (trace_id)
                        REFERENCES {tracesTable} (trace_id) ON DELETE CASCADE
                );

                CREATE INDEX ix_{SafeIndexName(spansTable)}_trace_id ON {spansTable} (trace_id);
            END;
            """;

        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string SafeIndexName(string tableName) => tableName.Replace("dbo.", string.Empty, StringComparison.Ordinal);
}

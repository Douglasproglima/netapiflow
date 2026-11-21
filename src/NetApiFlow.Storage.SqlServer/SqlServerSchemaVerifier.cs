using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace NetApiFlow.Storage.SqlServer;

internal static partial class SqlServerSchemaVerifier
{
    private const string RepositoryUrl = "https://github.com/Douglasproglima/netapiflow";

    public static async Task VerifyTracesAsync(
        SqlConnection connection, string tracesTable, string spansTable, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            var missing = new List<string>();
            if (!await TableExistsAsync(connection, tracesTable, cancellationToken))
            {
                missing.Add($"the table {tracesTable}");
            }
            else if (!await ColumnExistsAsync(connection, tracesTable, "route_key", cancellationToken))
            {
                missing.Add($"the column route_key of {tracesTable}");
            }

            if (!await TableExistsAsync(connection, spansTable, cancellationToken))
            {
                missing.Add($"the table {spansTable}");
            }

            ReportMissing(logger, missing);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.CheckFailed(logger, exception);
        }
    }

    public static async Task VerifyDeployAnnotationsAsync(
        SqlConnection connection, string deployAnnotationsTable, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            var missing = new List<string>();
            if (!await TableExistsAsync(connection, deployAnnotationsTable, cancellationToken))
            {
                missing.Add($"the table {deployAnnotationsTable}");
            }

            ReportMissing(logger, missing);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.CheckFailed(logger, exception);
        }
    }

    private static void ReportMissing(ILogger logger, List<string> missing)
    {
        if (missing.Count > 0)
        {
            Log.SchemaMissing(logger, string.Join(", ", missing), ScriptUrl());
        }
    }

    private static string ScriptUrl()
    {
        var informationalVersion = typeof(SqlServerSchemaVerifier).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        var version = informationalVersion.Split('+')[0];
        var reference = version.Length > 0 ? $"v{version}" : "main";
        return $"{RepositoryUrl}/blob/{reference}/src/NetApiFlow.Storage.SqlServer/schema.sql";
    }

    private static async Task<bool> TableExistsAsync(SqlConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@name, 'U') IS NULL THEN 0 ELSE 1 END", connection);
        command.Parameters.AddWithValue("name", table);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT CASE WHEN COL_LENGTH(@name, @column) IS NULL THEN 0 ELSE 1 END", connection);
        command.Parameters.AddWithValue("name", table);
        command.Parameters.AddWithValue("column", column);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "NetApiFlow.Storage.SqlServer: AutoCreateSchema is off and the schema could not be checked.")]
        public static partial void CheckFailed(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "NetApiFlow.Storage.SqlServer: AutoCreateSchema is off and the database is missing {Missing}. Nothing was created or changed. Run schema.sql ({ScriptUrl}) against this database: it creates and upgrades the tables and is safe to run again.")]
        public static partial void SchemaMissing(ILogger logger, string missing, string scriptUrl);
    }
}

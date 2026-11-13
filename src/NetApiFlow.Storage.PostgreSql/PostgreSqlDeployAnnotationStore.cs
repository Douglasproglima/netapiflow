using Npgsql;
using NpgsqlTypes;

namespace NetApiFlow.Storage.PostgreSql;

/// <summary><see cref="IDeployAnnotationStore"/> backed by PostgreSQL via raw ADO.NET (D-05).</summary>
internal sealed class PostgreSqlDeployAnnotationStore : IDeployAnnotationStore, IDisposable
{
    private readonly string _connectionString;
    private readonly string _deployAnnotationsTable;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public PostgreSqlDeployAnnotationStore(string connectionString, PostgreSqlStorageOptions options)
    {
        _connectionString = connectionString;
        _deployAnnotationsTable = PostgreSqlTableNames.ResolveDeployAnnotations(options.TablePrefix);
    }

    public async ValueTask SaveAsync(DeployAnnotation annotation, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand($"""
            INSERT INTO {_deployAnnotationsTable} (id, timestamp, label) VALUES (@id, @timestamp, @label)
            """, connection);
        command.Parameters.AddWithValue("id", annotation.Id);
        command.Parameters.AddWithValue("timestamp", NpgsqlDbType.TimestampTz, annotation.Timestamp);
        command.Parameters.AddWithValue("label", annotation.Label);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<DeployAnnotation>> QueryAsync(DateTime from, DateTime until, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand($"""
            SELECT id, timestamp, label FROM {_deployAnnotationsTable}
            WHERE timestamp >= @from AND timestamp <= @until
            ORDER BY timestamp
            """, connection);
        command.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, from);
        command.Parameters.AddWithValue("until", NpgsqlDbType.TimestampTz, until);

        var annotations = new List<DeployAnnotation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            annotations.Add(new DeployAnnotation
            {
                Id = reader.GetString(0),
                Timestamp = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                Label = reader.GetString(2),
            });
        }

        return annotations;
    }

    private async ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async ValueTask EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady)
        {
            return;
        }

        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await PostgreSqlSchemaInitializer.EnsureDeployAnnotationsCreatedAsync(connection, _deployAnnotationsTable, cancellationToken);
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    public void Dispose() => _schemaLock.Dispose();
}

using Microsoft.Data.SqlClient;

namespace NetApiFlow.Storage.SqlServer;

/// <summary><see cref="IDeployAnnotationStore"/> backed by SQL Server via raw ADO.NET (D-05).</summary>
internal sealed class SqlServerDeployAnnotationStore : IDeployAnnotationStore, IDisposable
{
    private readonly string _connectionString;
    private readonly string _deployAnnotationsTable;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public SqlServerDeployAnnotationStore(string connectionString, SqlServerStorageOptions options)
    {
        _connectionString = connectionString;
        _deployAnnotationsTable = SqlServerTableNames.ResolveDeployAnnotations(options.TablePrefix);
    }

    public async ValueTask SaveAsync(DeployAnnotation annotation, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var command = new SqlCommand($"""
            INSERT INTO {_deployAnnotationsTable} (id, timestamp, label) VALUES (@id, @timestamp, @label)
            """, connection);
        command.Parameters.AddWithValue("id", annotation.Id);
        command.Parameters.AddWithValue("timestamp", new DateTimeOffset(DateTime.SpecifyKind(annotation.Timestamp, DateTimeKind.Utc)));
        command.Parameters.AddWithValue("label", annotation.Label);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<DeployAnnotation>> QueryAsync(DateTime from, DateTime until, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var command = new SqlCommand($"""
            SELECT id, timestamp, label FROM {_deployAnnotationsTable}
            WHERE timestamp >= @from AND timestamp <= @until
            ORDER BY timestamp
            """, connection);
        command.Parameters.AddWithValue("from", new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Utc)));
        command.Parameters.AddWithValue("until", new DateTimeOffset(DateTime.SpecifyKind(until, DateTimeKind.Utc)));

        var annotations = new List<DeployAnnotation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            annotations.Add(new DeployAnnotation
            {
                Id = reader.GetString(0),
                Timestamp = reader.GetDateTimeOffset(1).UtcDateTime,
                Label = reader.GetString(2),
            });
        }

        return annotations;
    }

    private async ValueTask<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var connection = new SqlConnection(_connectionString);
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

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await SqlServerSchemaInitializer.EnsureDeployAnnotationsCreatedAsync(connection, _deployAnnotationsTable, cancellationToken);
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    public void Dispose() => _schemaLock.Dispose();
}

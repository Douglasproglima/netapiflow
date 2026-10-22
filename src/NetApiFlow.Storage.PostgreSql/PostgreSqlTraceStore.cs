using System.Text.Json;
using NetApiFlow.Domain;
using Npgsql;
using NpgsqlTypes;

namespace NetApiFlow.Storage.PostgreSql;

/// <summary>
/// <see cref="ITraceStore"/> backed by PostgreSQL via raw ADO.NET (ADR-0004, D-05). <see cref="SaveAsync"/>
/// uses <see cref="NpgsqlBinaryImporter"/> (the <c>COPY</c> protocol) for the batch write of a
/// trace and its spans.
/// </summary>
internal sealed class PostgreSqlTraceStore : ITraceStore, IDisposable
{
    private readonly string _connectionString;
    private readonly string _tracesTable;
    private readonly string _spansTable;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public PostgreSqlTraceStore(string connectionString, PostgreSqlStorageOptions options)
    {
        _connectionString = connectionString;
        (_tracesTable, _spansTable) = PostgreSqlTableNames.Resolve(options.TablePrefix);
    }

    public async ValueTask SaveAsync(FlowTrace trace, IReadOnlyList<FlowSpan> spans, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using (var importer = await connection.BeginBinaryImportAsync(
            $"COPY {_tracesTable} (trace_id, method, route_template, http_status, status, started_at, ended_at, duration_micros, span_count, truncated, error_message, tags) FROM STDIN (FORMAT BINARY)",
            cancellationToken))
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(trace.TraceId, NpgsqlDbType.Varchar, cancellationToken);
            await importer.WriteAsync(trace.Method, NpgsqlDbType.Varchar, cancellationToken);
            await importer.WriteAsync(trace.RouteTemplate, NpgsqlDbType.Text, cancellationToken);
            await WriteNullableAsync(importer, trace.HttpStatus, NpgsqlDbType.Integer, cancellationToken);
            await importer.WriteAsync(trace.Status.ToString(), NpgsqlDbType.Varchar, cancellationToken);
            await importer.WriteAsync(trace.StartedAt, NpgsqlDbType.TimestampTz, cancellationToken);
            await WriteNullableAsync(importer, trace.EndedAt, NpgsqlDbType.TimestampTz, cancellationToken);
            await WriteNullableAsync(importer, trace.DurationMicros, NpgsqlDbType.Bigint, cancellationToken);
            await importer.WriteAsync(trace.SpanCount, NpgsqlDbType.Integer, cancellationToken);
            await importer.WriteAsync(trace.Truncated, NpgsqlDbType.Boolean, cancellationToken);
            await WriteNullableAsync(importer, trace.ErrorMessage, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(SerializeTags(trace.Tags), NpgsqlDbType.Jsonb, cancellationToken);
            await importer.CompleteAsync(cancellationToken);
        }

        if (spans.Count == 0)
        {
            return;
        }

        await using var spanImporter = await connection.BeginBinaryImportAsync(
            $"COPY {_spansTable} (trace_id, span_id, parent_span_id, sequence, component, operation, type, started_at, ended_at, duration_micros, status, error_type, error_message, is_error_origin, tags) FROM STDIN (FORMAT BINARY)",
            cancellationToken);

        foreach (var span in spans)
        {
            await spanImporter.StartRowAsync(cancellationToken);
            await spanImporter.WriteAsync(span.TraceId, NpgsqlDbType.Varchar, cancellationToken);
            await spanImporter.WriteAsync(span.SpanId, NpgsqlDbType.Varchar, cancellationToken);
            await WriteNullableAsync(spanImporter, span.ParentSpanId, NpgsqlDbType.Varchar, cancellationToken);
            await spanImporter.WriteAsync(span.Sequence, NpgsqlDbType.Integer, cancellationToken);
            await spanImporter.WriteAsync(span.Component, NpgsqlDbType.Text, cancellationToken);
            await spanImporter.WriteAsync(span.Operation, NpgsqlDbType.Text, cancellationToken);
            await spanImporter.WriteAsync(span.Type.ToString(), NpgsqlDbType.Varchar, cancellationToken);
            await spanImporter.WriteAsync(span.StartedAt, NpgsqlDbType.TimestampTz, cancellationToken);
            await WriteNullableAsync(spanImporter, span.EndedAt, NpgsqlDbType.TimestampTz, cancellationToken);
            await WriteNullableAsync(spanImporter, span.DurationMicros, NpgsqlDbType.Bigint, cancellationToken);
            await spanImporter.WriteAsync(span.Status.ToString(), NpgsqlDbType.Varchar, cancellationToken);
            await WriteNullableAsync(spanImporter, span.ErrorType, NpgsqlDbType.Text, cancellationToken);
            await WriteNullableAsync(spanImporter, span.ErrorMessage, NpgsqlDbType.Text, cancellationToken);
            await spanImporter.WriteAsync(span.IsErrorOrigin, NpgsqlDbType.Boolean, cancellationToken);
            await spanImporter.WriteAsync(SerializeTags(span.Tags), NpgsqlDbType.Jsonb, cancellationToken);
        }

        await spanImporter.CompleteAsync(cancellationToken);
    }

    public async ValueTask<FlowTraceDetail?> GetAsync(string traceId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var trace = await ReadTraceAsync(connection, traceId, cancellationToken);
        if (trace is null)
        {
            return null;
        }

        var spans = await ReadSpansAsync(connection, traceId, cancellationToken);
        return new FlowTraceDetail { Trace = trace, Spans = spans };
    }

    public async ValueTask<PagedResult<FlowTraceSummary>> QueryAsync(TraceQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var countCommand = new NpgsqlCommand { Connection = connection };
        var whereForCount = AddFilterParameters(countCommand, query);
        countCommand.CommandText = $"SELECT COUNT(*) FROM {_tracesTable} {whereForCount}";
        var totalCount = (long)(await countCommand.ExecuteScalarAsync(cancellationToken))!;

        var page = Math.Max(query.Page, 1);
        await using var pageCommand = new NpgsqlCommand { Connection = connection };
        var whereForPage = AddFilterParameters(pageCommand, query);
        pageCommand.CommandText = $"""
            SELECT trace_id, method, route_template, http_status, status, started_at, duration_micros, truncated
            FROM {_tracesTable} {whereForPage}
            {BuildOrderBy(query)}
            LIMIT @pageSize OFFSET @offset
            """;
        pageCommand.Parameters.AddWithValue("pageSize", query.PageSize);
        pageCommand.Parameters.AddWithValue("offset", (page - 1) * query.PageSize);

        var items = new List<FlowTraceSummary>();
        await using (var reader = await pageCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new FlowTraceSummary
                {
                    TraceId = reader.GetString(0),
                    Method = reader.GetString(1),
                    RouteTemplate = reader.GetString(2),
                    HttpStatus = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    Status = Enum.Parse<FlowStatus>(reader.GetString(4)),
                    StartedAt = ReadUtc(reader, 5),
                    DurationMicros = reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    Truncated = reader.GetBoolean(7),
                });
            }
        }

        return new PagedResult<FlowTraceSummary> { Items = items, TotalCount = (int)totalCount };
    }

    public async ValueTask<IReadOnlyList<ComponentStat>> GetComponentStatsAsync(StatsQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand($"""
            SELECT s.component, s.operation, s.status, s.duration_micros
            FROM {_spansTable} s
            JOIN {_tracesTable} t ON s.trace_id = t.trace_id
            WHERE (@from::timestamptz IS NULL OR t.started_at >= @from) AND (@to::timestamptz IS NULL OR t.started_at <= @to)
            """, connection);
        command.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, (object?)query.From ?? DBNull.Value);
        command.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, (object?)query.To ?? DBNull.Value);

        var rows = new List<(string Component, string Operation, FlowStatus Status, long? Duration)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    Enum.Parse<FlowStatus>(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetInt64(3)));
            }
        }

        return rows
            .GroupBy(r => (r.Component, r.Operation))
            .Select(group =>
            {
                var durations = group.Where(r => r.Duration is not null).Select(r => r.Duration!.Value).OrderBy(d => d).ToList();
                return new ComponentStat
                {
                    Component = group.Key.Component,
                    Operation = group.Key.Operation,
                    ExecutionCount = group.Count(),
                    ErrorCount = group.Count(r => r.Status == FlowStatus.Error),
                    MedianDurationMicros = Percentile(durations, 0.5),
                    AverageDurationMicros = durations.Count == 0 ? 0 : (long)durations.Average(),
                    P95DurationMicros = Percentile(durations, 0.95),
                    MaxDurationMicros = durations.Count == 0 ? 0 : durations[^1],
                };
            })
            .ToList<ComponentStat>();
    }

    public async ValueTask<TraceStats> GetTraceStatsAsync(StatsQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand($"""
            SELECT status, duration_micros FROM {_tracesTable}
            WHERE (@from::timestamptz IS NULL OR started_at >= @from) AND (@to::timestamptz IS NULL OR started_at <= @to)
            """, connection);
        command.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, (object?)query.From ?? DBNull.Value);
        command.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, (object?)query.To ?? DBNull.Value);

        var statuses = new List<FlowStatus>();
        var durations = new List<long>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                statuses.Add(Enum.Parse<FlowStatus>(reader.GetString(0)));
                if (!reader.IsDBNull(1))
                {
                    durations.Add(reader.GetInt64(1));
                }
            }
        }

        durations.Sort();

        return new TraceStats
        {
            TotalTraces = statuses.Count,
            ErrorCount = statuses.Count(s => s == FlowStatus.Error),
            AverageDurationMicros = durations.Count == 0 ? 0 : (long)durations.Average(),
            P95DurationMicros = Percentile(durations, 0.95),
            MaxDurationMicros = durations.Count == 0 ? 0 : durations[^1],
        };
    }

    public async ValueTask<FlowTraceDetail?> GetLastSuccessfulAsync(string method, string routeTemplate, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand($"""
            SELECT trace_id, method, route_template, http_status, status, started_at, ended_at, duration_micros, span_count, truncated, error_message, tags
            FROM {_tracesTable}
            WHERE method ILIKE @method AND route_template = @route AND status = 'Success'
            ORDER BY started_at DESC
            LIMIT 1
            """, connection);
        command.Parameters.AddWithValue("method", method);
        command.Parameters.AddWithValue("route", routeTemplate);

        FlowTrace? trace;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            trace = await reader.ReadAsync(cancellationToken) ? MapTrace(reader) : null;
        }

        if (trace is null)
        {
            return null;
        }

        var spans = await ReadSpansAsync(connection, trace.TraceId, cancellationToken);
        return new FlowTraceDetail { Trace = trace, Spans = spans };
    }

    public async ValueTask<int> ApplyRetentionAsync(RetentionPolicy policy, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var cutoff = DateTime.UtcNow - policy.MaxAge;

        await using var command = new NpgsqlCommand($"""
            WITH ranked AS (
                SELECT trace_id, ROW_NUMBER() OVER (ORDER BY started_at DESC) AS rn
                FROM {_tracesTable}
            ),
            doomed AS (
                SELECT trace_id FROM ranked WHERE rn > @maxTraces
                UNION
                SELECT trace_id FROM {_tracesTable} WHERE started_at < @cutoff
            )
            DELETE FROM {_tracesTable} WHERE trace_id IN (SELECT trace_id FROM doomed)
            """, connection);
        command.Parameters.AddWithValue("maxTraces", policy.MaxTraces);
        command.Parameters.AddWithValue("cutoff", NpgsqlDbType.TimestampTz, cutoff);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string AddFilterParameters(NpgsqlCommand command, TraceQuery query)
    {
        var conditions = new List<string>();

        if (query.Status is not null)
        {
            conditions.Add("status = @status");
            command.Parameters.AddWithValue("status", query.Status.Value.ToString());
        }

        if (query.Method is not null)
        {
            conditions.Add("method ILIKE @method");
            command.Parameters.AddWithValue("method", query.Method);
        }

        if (query.RouteTemplate is not null)
        {
            conditions.Add("route_template = @route");
            command.Parameters.AddWithValue("route", query.RouteTemplate);
        }

        if (query.Tenant is not null)
        {
            conditions.Add("tags ->> 'tenant' = @tenant");
            command.Parameters.AddWithValue("tenant", query.Tenant);
        }

        if (query.From is not null)
        {
            conditions.Add("started_at >= @from");
            command.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, query.From.Value);
        }

        if (query.To is not null)
        {
            conditions.Add("started_at <= @to");
            command.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, query.To.Value);
        }

        if (query.MinDurationMicros is not null)
        {
            conditions.Add("COALESCE(duration_micros, 0) >= @minDuration");
            command.Parameters.AddWithValue("minDuration", query.MinDurationMicros.Value);
        }

        return conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
    }

    /// <summary>
    /// Builds the <c>ORDER BY</c> clause from <paramref name="query"/>'s already-parsed sort enum,
    /// never from concatenated query-string text (D-41). Ties break by <c>started_at</c>
    /// descending, then <c>trace_id</c>, so paginating a result set with repeated values never
    /// skips or repeats a row across pages.
    /// </summary>
    private static string BuildOrderBy(TraceQuery query)
    {
        var field = query.SortBy ?? TraceSortField.StartedAt;
        var ascending = query.SortDirection == global::NetApiFlow.SortDirection.Ascending;
        var direction = ascending ? "ASC" : "DESC";

        var primary = field switch
        {
            TraceSortField.Duration => $"duration_micros {direction} NULLS LAST",
            TraceSortField.HttpStatus => $"http_status {direction} NULLS LAST",
            TraceSortField.Method => $"LOWER(method) {direction}",
            TraceSortField.Route => $"LOWER(route_template) {direction}",
            _ => $"started_at {direction}",
        };

        return field == TraceSortField.StartedAt
            ? $"ORDER BY {primary}, trace_id"
            : $"ORDER BY {primary}, started_at DESC, trace_id";
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
            await PostgreSqlSchemaInitializer.EnsureCreatedAsync(connection, _tracesTable, _spansTable, cancellationToken);
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private async Task<FlowTrace?> ReadTraceAsync(NpgsqlConnection connection, string traceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT trace_id, method, route_template, http_status, status, started_at, ended_at, duration_micros, span_count, truncated, error_message, tags
            FROM {_tracesTable} WHERE trace_id = @traceId
            """, connection);
        command.Parameters.AddWithValue("traceId", traceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapTrace(reader) : null;
    }

    private async Task<List<FlowSpan>> ReadSpansAsync(NpgsqlConnection connection, string traceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT trace_id, span_id, parent_span_id, sequence, component, operation, type, started_at, ended_at, duration_micros, status, error_type, error_message, is_error_origin, tags
            FROM {_spansTable} WHERE trace_id = @traceId ORDER BY sequence
            """, connection);
        command.Parameters.AddWithValue("traceId", traceId);

        var spans = new List<FlowSpan>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            spans.Add(MapSpan(reader));
        }

        return spans;
    }

    private static FlowTrace MapTrace(NpgsqlDataReader reader) => new()
    {
        TraceId = reader.GetString(0),
        Method = reader.GetString(1),
        RouteTemplate = reader.GetString(2),
        HttpStatus = reader.IsDBNull(3) ? null : reader.GetInt32(3),
        Status = Enum.Parse<FlowStatus>(reader.GetString(4)),
        StartedAt = ReadUtc(reader, 5),
        EndedAt = reader.IsDBNull(6) ? null : ReadUtc(reader, 6),
        DurationMicros = reader.IsDBNull(7) ? null : reader.GetInt64(7),
        SpanCount = reader.GetInt32(8),
        Truncated = reader.GetBoolean(9),
        ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10),
        Tags = DeserializeTags(reader.GetString(11)),
    };

    private static FlowSpan MapSpan(NpgsqlDataReader reader) => new()
    {
        TraceId = reader.GetString(0),
        SpanId = reader.GetString(1),
        ParentSpanId = reader.IsDBNull(2) ? null : reader.GetString(2),
        Sequence = reader.GetInt32(3),
        Component = reader.GetString(4),
        Operation = reader.GetString(5),
        Type = Enum.Parse<ComponentType>(reader.GetString(6)),
        StartedAt = ReadUtc(reader, 7),
        EndedAt = reader.IsDBNull(8) ? null : ReadUtc(reader, 8),
        DurationMicros = reader.IsDBNull(9) ? null : reader.GetInt64(9),
        Status = Enum.Parse<FlowStatus>(reader.GetString(10)),
        ErrorType = reader.IsDBNull(11) ? null : reader.GetString(11),
        ErrorMessage = reader.IsDBNull(12) ? null : reader.GetString(12),
        IsErrorOrigin = reader.GetBoolean(13),
        Tags = DeserializeTags(reader.GetString(14)),
    };

    private static DateTime ReadUtc(NpgsqlDataReader reader, int ordinal) =>
        DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);

    private static async Task WriteNullableAsync<T>(NpgsqlBinaryImporter importer, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        where T : struct
    {
        if (value is null)
        {
            await importer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await importer.WriteAsync(value.Value, type, cancellationToken);
        }
    }

    private static async Task WriteNullableAsync(NpgsqlBinaryImporter importer, string? value, NpgsqlDbType type, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await importer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await importer.WriteAsync(value, type, cancellationToken);
        }
    }

    private static string SerializeTags(IReadOnlyDictionary<string, string> tags) => JsonSerializer.Serialize(tags);

    private static Dictionary<string, string> DeserializeTags(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    public void Dispose() => _schemaLock.Dispose();

    /// <summary>Same formula as <c>InMemoryTraceStore</c>, kept consistent across every <see cref="ITraceStore"/> implementation.</summary>
    private static long Percentile(List<long> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Count - 1)];
    }
}

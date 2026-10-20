using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using NetApiFlow.Domain;

namespace NetApiFlow.Storage.SqlServer;

/// <summary>
/// <see cref="ITraceStore"/> backed by SQL Server via raw ADO.NET (ADR-0004, D-05). <see cref="SaveAsync"/>
/// uses <see cref="SqlBulkCopy"/> for the batch write of a trace and its spans.
/// </summary>
internal sealed class SqlServerTraceStore : ITraceStore, IDisposable
{
    private readonly string _connectionString;
    private readonly string _tracesTable;
    private readonly string _spansTable;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public SqlServerTraceStore(string connectionString, SqlServerStorageOptions options)
    {
        _connectionString = connectionString;
        (_tracesTable, _spansTable) = SqlServerTableNames.Resolve(options.TablePrefix);
    }

    public async ValueTask SaveAsync(FlowTrace trace, IReadOnlyList<FlowSpan> spans, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        using (var bulkCopy = new SqlBulkCopy(connection) { DestinationTableName = _tracesTable })
        {
            MapTraceColumns(bulkCopy);
            await bulkCopy.WriteToServerAsync(BuildTraceTable(trace), cancellationToken);
        }

        if (spans.Count == 0)
        {
            return;
        }

        using var spanBulkCopy = new SqlBulkCopy(connection) { DestinationTableName = _spansTable };
        MapSpanColumns(spanBulkCopy);
        await spanBulkCopy.WriteToServerAsync(BuildSpansTable(spans), cancellationToken);
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

        await using var countCommand = new SqlCommand { Connection = connection };
        var whereForCount = AddFilterParameters(countCommand, query);
        countCommand.CommandText = $"SELECT COUNT(*) FROM {_tracesTable} {whereForCount}";
        var totalCount = (int)(await countCommand.ExecuteScalarAsync(cancellationToken))!;

        var page = Math.Max(query.Page, 1);
        await using var pageCommand = new SqlCommand { Connection = connection };
        var whereForPage = AddFilterParameters(pageCommand, query);
        pageCommand.CommandText = $"""
            SELECT trace_id, method, route_template, http_status, status, started_at, duration_micros, truncated
            FROM {_tracesTable} {whereForPage}
            ORDER BY started_at DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
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

        return new PagedResult<FlowTraceSummary> { Items = items, TotalCount = totalCount };
    }

    public async ValueTask<IReadOnlyList<ComponentStat>> GetComponentStatsAsync(StatsQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var command = new SqlCommand($"""
            SELECT s.component, s.operation, s.status, s.duration_micros
            FROM {_spansTable} s
            JOIN {_tracesTable} t ON s.trace_id = t.trace_id
            WHERE (@from IS NULL OR t.started_at >= @from) AND (@to IS NULL OR t.started_at <= @to)
            """, connection);
        command.Parameters.AddWithValue("from", (object?)query.From ?? DBNull.Value);
        command.Parameters.AddWithValue("to", (object?)query.To ?? DBNull.Value);

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

        await using var command = new SqlCommand($"""
            SELECT status, duration_micros FROM {_tracesTable}
            WHERE (@from IS NULL OR started_at >= @from) AND (@to IS NULL OR started_at <= @to)
            """, connection);
        command.Parameters.AddWithValue("from", (object?)query.From ?? DBNull.Value);
        command.Parameters.AddWithValue("to", (object?)query.To ?? DBNull.Value);

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

        await using var command = new SqlCommand($"""
            SELECT TOP (1) trace_id, method, route_template, http_status, status, started_at, ended_at, duration_micros, span_count, truncated, error_message, tags
            FROM {_tracesTable}
            WHERE method = @method AND route_template = @route AND status = 'Success'
            ORDER BY started_at DESC
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

        await using var command = new SqlCommand($"""
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
        command.Parameters.AddWithValue("cutoff", cutoff);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string AddFilterParameters(SqlCommand command, TraceQuery query)
    {
        var conditions = new List<string>();

        if (query.Status is not null)
        {
            conditions.Add("status = @status");
            command.Parameters.AddWithValue("status", query.Status.Value.ToString());
        }

        if (query.Method is not null)
        {
            conditions.Add("method = @method");
            command.Parameters.AddWithValue("method", query.Method);
        }

        if (query.RouteTemplate is not null)
        {
            conditions.Add("route_template = @route");
            command.Parameters.AddWithValue("route", query.RouteTemplate);
        }

        if (query.Tenant is not null)
        {
            conditions.Add("JSON_VALUE(tags, '$.tenant') = @tenant");
            command.Parameters.AddWithValue("tenant", query.Tenant);
        }

        if (query.From is not null)
        {
            conditions.Add("started_at >= @from");
            command.Parameters.AddWithValue("from", query.From.Value);
        }

        if (query.To is not null)
        {
            conditions.Add("started_at <= @to");
            command.Parameters.AddWithValue("to", query.To.Value);
        }

        if (query.MinDurationMicros is not null)
        {
            conditions.Add("COALESCE(duration_micros, 0) >= @minDuration");
            command.Parameters.AddWithValue("minDuration", query.MinDurationMicros.Value);
        }

        return conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
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
            await SqlServerSchemaInitializer.EnsureCreatedAsync(connection, _tracesTable, _spansTable, cancellationToken);
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private async Task<FlowTrace?> ReadTraceAsync(SqlConnection connection, string traceId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand($"""
            SELECT trace_id, method, route_template, http_status, status, started_at, ended_at, duration_micros, span_count, truncated, error_message, tags
            FROM {_tracesTable} WHERE trace_id = @traceId
            """, connection);
        command.Parameters.AddWithValue("traceId", traceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapTrace(reader) : null;
    }

    private async Task<List<FlowSpan>> ReadSpansAsync(SqlConnection connection, string traceId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand($"""
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

    private static FlowTrace MapTrace(SqlDataReader reader) => new()
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

    private static FlowSpan MapSpan(SqlDataReader reader) => new()
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

    private static DateTime ReadUtc(SqlDataReader reader, int ordinal) =>
        reader.GetDateTimeOffset(ordinal).UtcDateTime;

    private static DataTable BuildTraceTable(FlowTrace trace)
    {
        var table = new DataTable();
        table.Columns.Add("trace_id", typeof(string));
        table.Columns.Add("method", typeof(string));
        table.Columns.Add("route_template", typeof(string));
        table.Columns.Add("http_status", typeof(int));
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("started_at", typeof(DateTimeOffset));
        table.Columns.Add("ended_at", typeof(DateTimeOffset));
        table.Columns.Add("duration_micros", typeof(long));
        table.Columns.Add("span_count", typeof(int));
        table.Columns.Add("truncated", typeof(bool));
        table.Columns.Add("error_message", typeof(string));
        table.Columns.Add("tags", typeof(string));

        table.Rows.Add(
            trace.TraceId,
            trace.Method,
            trace.RouteTemplate,
            (object?)trace.HttpStatus ?? DBNull.Value,
            trace.Status.ToString(),
            new DateTimeOffset(DateTime.SpecifyKind(trace.StartedAt, DateTimeKind.Utc)),
            trace.EndedAt is null ? DBNull.Value : new DateTimeOffset(DateTime.SpecifyKind(trace.EndedAt.Value, DateTimeKind.Utc)),
            (object?)trace.DurationMicros ?? DBNull.Value,
            trace.SpanCount,
            trace.Truncated,
            (object?)trace.ErrorMessage ?? DBNull.Value,
            SerializeTags(trace.Tags));

        return table;
    }

    private static DataTable BuildSpansTable(IReadOnlyList<FlowSpan> spans)
    {
        var table = new DataTable();
        table.Columns.Add("trace_id", typeof(string));
        table.Columns.Add("span_id", typeof(string));
        table.Columns.Add("parent_span_id", typeof(string));
        table.Columns.Add("sequence", typeof(int));
        table.Columns.Add("component", typeof(string));
        table.Columns.Add("operation", typeof(string));
        table.Columns.Add("type", typeof(string));
        table.Columns.Add("started_at", typeof(DateTimeOffset));
        table.Columns.Add("ended_at", typeof(DateTimeOffset));
        table.Columns.Add("duration_micros", typeof(long));
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("error_type", typeof(string));
        table.Columns.Add("error_message", typeof(string));
        table.Columns.Add("is_error_origin", typeof(bool));
        table.Columns.Add("tags", typeof(string));

        foreach (var span in spans)
        {
            table.Rows.Add(
                span.TraceId,
                span.SpanId,
                (object?)span.ParentSpanId ?? DBNull.Value,
                span.Sequence,
                span.Component,
                span.Operation,
                span.Type.ToString(),
                new DateTimeOffset(DateTime.SpecifyKind(span.StartedAt, DateTimeKind.Utc)),
                span.EndedAt is null ? DBNull.Value : new DateTimeOffset(DateTime.SpecifyKind(span.EndedAt.Value, DateTimeKind.Utc)),
                (object?)span.DurationMicros ?? DBNull.Value,
                span.Status.ToString(),
                (object?)span.ErrorType ?? DBNull.Value,
                (object?)span.ErrorMessage ?? DBNull.Value,
                span.IsErrorOrigin,
                SerializeTags(span.Tags));
        }

        return table;
    }

    private static void MapTraceColumns(SqlBulkCopy bulkCopy)
    {
        foreach (var column in new[]
        {
            "trace_id", "method", "route_template", "http_status", "status", "started_at",
            "ended_at", "duration_micros", "span_count", "truncated", "error_message", "tags",
        })
        {
            bulkCopy.ColumnMappings.Add(column, column);
        }
    }

    private static void MapSpanColumns(SqlBulkCopy bulkCopy)
    {
        foreach (var column in new[]
        {
            "trace_id", "span_id", "parent_span_id", "sequence", "component", "operation", "type",
            "started_at", "ended_at", "duration_micros", "status", "error_type", "error_message",
            "is_error_origin", "tags",
        })
        {
            bulkCopy.ColumnMappings.Add(column, column);
        }
    }

    private static string SerializeTags(IReadOnlyDictionary<string, string> tags) => JsonSerializer.Serialize(tags);

    private static Dictionary<string, string> DeserializeTags(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

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

    public void Dispose() => _schemaLock.Dispose();
}

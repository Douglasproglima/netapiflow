using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Domain;
using NetApiFlow.Storage.SqlServer;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

[Collection(MsSqlTestGroup.Name)]
public sealed class SqlServerTraceStoreTests(MsSqlContainerFixture fixture) : TraceStoreTestsBase
{
    protected override ITraceStore CreateStore() => CreateStore($"t{Guid.NewGuid():N}_");

    private ITraceStore CreateStore(string tablePrefix)
    {
        var services = new ServiceCollection();
        services.AddNetApiFlowSqlServerStorage(fixture.ConnectionString, o => o.TablePrefix = tablePrefix);
        return services.BuildServiceProvider().GetRequiredService<ITraceStore>();
    }

    /// <summary>
    /// The sibling test in <see cref="TraceStoreTestsBase"/> passes on a default SQL Server
    /// install either way, because the default collation is case-insensitive and hides the
    /// difference. This one forces the column to a case-sensitive collation, which is what the
    /// `UPPER(method) = UPPER(@method)` comparison is there for: the store has to behave the same
    /// on a server somebody installed with a different collation.
    /// </summary>
    [Fact]
    public async Task QueriesFilteringByMethod_MatchRegardlessOfCasing_UnderACaseSensitiveCollation()
    {
        var tablePrefix = $"t{Guid.NewGuid():N}_";
        var store = CreateStore(tablePrefix);
        var trace = new FlowTrace
        {
            TraceId = "cs-collation-1",
            Method = "get",
            RouteTemplate = "/api/orders/{id}",
            HttpStatus = 200,
            Status = FlowStatus.Success,
            StartedAt = DateTime.UtcNow,
            EndedAt = DateTime.UtcNow.AddMilliseconds(1),
            DurationMicros = 1_000,
            SpanCount = 1,
            Truncated = false,
            ErrorMessage = null,
            Tags = new Dictionary<string, string>(),
        };
        var span = new FlowSpan
        {
            TraceId = trace.TraceId,
            SpanId = "s1",
            ParentSpanId = null,
            Sequence = 0,
            Component = "OrderRepository",
            Operation = "FindAsync",
            Type = ComponentType.Repository,
            StartedAt = trace.StartedAt,
            EndedAt = trace.EndedAt,
            DurationMicros = 1_000,
            Status = FlowStatus.Success,
            ErrorType = null,
            ErrorMessage = null,
            IsErrorOrigin = false,
            Tags = new Dictionary<string, string>(),
            Logs = [],
        };

        await store.SaveAsync(trace, [span], CancellationToken.None);
        await MakeMethodColumnCaseSensitiveAsync($"{tablePrefix}traces");

        var queried = await store.QueryAsync(new TraceQuery { Method = "GET" }, CancellationToken.None);
        queried.Items.ShouldHaveSingleItem().TraceId.ShouldBe(trace.TraceId);

        var lastSuccessful = await store.GetLastSuccessfulAsync("GET", trace.RouteTemplate, CancellationToken.None);
        lastSuccessful.ShouldNotBeNull().Trace.TraceId.ShouldBe(trace.TraceId);

        var map = await store.GetArchitectureMapAsync("GET", trace.RouteTemplate, new StatsQuery(), CancellationToken.None);
        map.TotalExecutions.ShouldBe(1);
        map.Nodes.ShouldNotBeEmpty();
    }

    private async Task MakeMethodColumnCaseSensitiveAsync(string tableName)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var dropIndexes = new SqlCommand("""
            DECLARE @drop nvarchar(max) = N'';
            SELECT @drop = @drop + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.' + QUOTENAME(t.name) + N';'
            FROM sys.indexes i
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE t.name = @table AND c.name = 'method' AND i.type > 0 AND i.is_primary_key = 0;
            EXEC sp_executesql @drop;
            """, connection);
        dropIndexes.Parameters.AddWithValue("table", tableName);
        await dropIndexes.ExecuteNonQueryAsync();

        await using var alterColumn = new SqlCommand(
            $"ALTER TABLE dbo.[{tableName}] ALTER COLUMN method varchar(16) COLLATE Latin1_General_CS_AS NOT NULL;",
            connection);
        await alterColumn.ExecuteNonQueryAsync();
    }
}

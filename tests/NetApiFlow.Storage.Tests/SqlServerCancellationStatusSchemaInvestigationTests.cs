using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NetApiFlow.Domain;
using NetApiFlow.Storage.SqlServer;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

/// <summary>
/// Confirms, against the real SQL Server schema, that storing a <c>status</c> value
/// outside the three <see cref="FlowStatus"/> currently defines requires no migration. Reading
/// <c>schema.sql</c> already shows no <c>CHECK</c> constraint on the column, but only a real
/// write/read round-trip proves nothing in the driver or server rejects it.
/// </summary>
[Collection(MsSqlTestGroup.Name)]
public sealed class SqlServerCancellationStatusSchemaInvestigationTests(MsSqlContainerFixture fixture)
{
    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Timeout")]
    public async Task Status_WithAValueOutsideTheCurrentEnum_RoundTripsWithoutSchemaChange(string candidateStatus)
    {
        var tablePrefix = $"t{Guid.NewGuid():N}_";
        var services = new ServiceCollection();
        services.AddNetApiFlowSqlServerStorage(fixture.ConnectionString, o => o.TablePrefix = tablePrefix);
        var store = services.BuildServiceProvider().GetRequiredService<ITraceStore>();

        var traceId = Guid.NewGuid().ToString("N");
        await store.SaveAsync(MakeTrace(traceId), [], CancellationToken.None);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using (var update = new SqlCommand($"UPDATE dbo.{tablePrefix}traces SET status = @status WHERE trace_id = @traceId", connection))
        {
            update.Parameters.AddWithValue("@status", candidateStatus);
            update.Parameters.AddWithValue("@traceId", traceId);
            (await update.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        await using var select = new SqlCommand($"SELECT status FROM dbo.{tablePrefix}traces WHERE trace_id = @traceId", connection);
        select.Parameters.AddWithValue("@traceId", traceId);
        var storedStatus = (string)(await select.ExecuteScalarAsync())!;

        storedStatus.ShouldBe(candidateStatus);
    }

    private static FlowTrace MakeTrace(string traceId) => new()
    {
        TraceId = traceId,
        Method = "GET",
        RouteTemplate = "/api/orders/{id}",
        HttpStatus = 200,
        Status = FlowStatus.Success,
        StartedAt = DateTime.UtcNow,
        EndedAt = DateTime.UtcNow,
        DurationMicros = 1_000,
        SpanCount = 0,
        Truncated = false,
        ErrorMessage = null,
        Tags = new Dictionary<string, string>(),
    };
}

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetApiFlow.Storage.SqlServer;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

[Collection(MsSqlTestGroup.Name)]
public sealed class SqlServerSchemaVerificationRetryTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task TraceStore_WhenTheSchemaCheckCouldNotRun_ChecksAgainOnTheNextCallUntilItCompletes()
    {
        var tablePrefix = NewPrefix();
        await RunScriptAsync(tablePrefix);
        var check = new ScriptedSchemaCheck<SqlConnection>(false, true);
        using var store = new SqlServerTraceStore(fixture.ConnectionString, OptionsWithoutAutoCreate(tablePrefix), NullLogger.Instance, check.Run);

        await store.GetAsync("first", CancellationToken.None);
        await store.GetAsync("second", CancellationToken.None);
        await store.GetAsync("third", CancellationToken.None);

        check.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task DeployAnnotationStore_WhenTheSchemaCheckCouldNotRun_ChecksAgainOnTheNextCallUntilItCompletes()
    {
        var tablePrefix = NewPrefix();
        await RunScriptAsync(tablePrefix);
        var check = new ScriptedSchemaCheck<SqlConnection>(false, true);
        using var store = new SqlServerDeployAnnotationStore(fixture.ConnectionString, OptionsWithoutAutoCreate(tablePrefix), NullLogger.Instance, check.Run);

        await store.QueryAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, CancellationToken.None);
        await store.QueryAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, CancellationToken.None);
        await store.QueryAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, CancellationToken.None);

        check.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Verifier_WhenTheConnectionIsClosed_ReturnsFalseAndLogsOncePerInterval()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.CloseAsync();
        var logs = new CapturingLoggerProvider();
        var limiter = new IntervalLogLimiter(TimeSpan.FromMinutes(1));

        var first = await SqlServerSchemaVerifier.VerifyTracesAsync(connection, "t_traces", "t_spans", logs.CreateLogger("test"), limiter, CancellationToken.None);
        var second = await SqlServerSchemaVerifier.VerifyDeployAnnotationsAsync(connection, "t_deploys", logs.CreateLogger("test"), limiter, CancellationToken.None);

        first.ShouldBeFalse();
        second.ShouldBeFalse();
        logs.Lines.Count(line => line.Level == LogLevel.Error).ShouldBe(1);
    }

    [Fact]
    public async Task Verifier_WhenTheTablesAreMissing_ReportsThemAndStillCountsAsCompleted()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var logs = new CapturingLoggerProvider();
        var limiter = new IntervalLogLimiter(TimeSpan.FromMinutes(1));

        var completed = await SqlServerSchemaVerifier.VerifyTracesAsync(connection, $"{NewPrefix()}traces", $"{NewPrefix()}spans", logs.CreateLogger("test"), limiter, CancellationToken.None);

        completed.ShouldBeTrue();
        logs.Lines.Count(line => line.Level == LogLevel.Error && line.Message.Contains("schema.sql", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task Verifier_WhenTheSchemaIsThere_CompletesWithoutLoggingAnything()
    {
        var tablePrefix = NewPrefix();
        await RunScriptAsync(tablePrefix);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var logs = new CapturingLoggerProvider();

        var completed = await SqlServerSchemaVerifier.VerifyTracesAsync(
            connection, $"{tablePrefix}traces", $"{tablePrefix}spans", logs.CreateLogger("test"), new IntervalLogLimiter(TimeSpan.FromMinutes(1)), CancellationToken.None);

        completed.ShouldBeTrue();
        logs.Lines.ShouldBeEmpty();
    }

    private static string NewPrefix() => $"v{Guid.NewGuid():N}_";

    private static SqlServerStorageOptions OptionsWithoutAutoCreate(string tablePrefix) =>
        new() { TablePrefix = tablePrefix, AutoCreateSchema = false };

    private async Task RunScriptAsync(string tablePrefix)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in StorageSchemaScripts.SplitIntoBatches(StorageSchemaScripts.SqlServer(tablePrefix)))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

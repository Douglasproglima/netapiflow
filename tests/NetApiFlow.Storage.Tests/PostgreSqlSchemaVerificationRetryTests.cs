using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetApiFlow.Storage.PostgreSql;
using Npgsql;
using Shouldly;

namespace NetApiFlow.Storage.Tests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class PostgreSqlSchemaVerificationRetryTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task TraceStore_WhenTheSchemaCheckCouldNotRun_ChecksAgainOnTheNextCallUntilItCompletes()
    {
        var tablePrefix = NewPrefix();
        await RunScriptAsync(tablePrefix);
        var check = new ScriptedSchemaCheck<NpgsqlConnection>(false, true);
        using var store = new PostgreSqlTraceStore(fixture.ConnectionString, OptionsWithoutAutoCreate(tablePrefix), NullLogger.Instance, check.Run);

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
        var check = new ScriptedSchemaCheck<NpgsqlConnection>(false, true);
        using var store = new PostgreSqlDeployAnnotationStore(fixture.ConnectionString, OptionsWithoutAutoCreate(tablePrefix), NullLogger.Instance, check.Run);

        await store.QueryAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, CancellationToken.None);
        await store.QueryAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, CancellationToken.None);
        await store.QueryAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, CancellationToken.None);

        check.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Verifier_WhenTheConnectionIsClosed_ReturnsFalseAndLogsOncePerInterval()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.CloseAsync();
        var logs = new CapturingLoggerProvider();
        var limiter = new IntervalLogLimiter(TimeSpan.FromMinutes(1));

        var first = await PostgreSqlSchemaVerifier.VerifyTracesAsync(connection, "t_traces", "t_spans", logs.CreateLogger("test"), limiter, CancellationToken.None);
        var second = await PostgreSqlSchemaVerifier.VerifyDeployAnnotationsAsync(connection, "t_deploys", logs.CreateLogger("test"), limiter, CancellationToken.None);

        first.ShouldBeFalse();
        second.ShouldBeFalse();
        logs.Lines.Count(line => line.Level == LogLevel.Error).ShouldBe(1);
    }

    [Fact]
    public async Task Verifier_WhenTheTablesAreMissing_ReportsThemAndStillCountsAsCompleted()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var logs = new CapturingLoggerProvider();
        var limiter = new IntervalLogLimiter(TimeSpan.FromMinutes(1));

        var completed = await PostgreSqlSchemaVerifier.VerifyTracesAsync(connection, $"{NewPrefix()}traces", $"{NewPrefix()}spans", logs.CreateLogger("test"), limiter, CancellationToken.None);

        completed.ShouldBeTrue();
        logs.Lines.Count(line => line.Level == LogLevel.Error && line.Message.Contains("schema.sql", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task Verifier_WhenTheSchemaIsThere_CompletesWithoutLoggingAnything()
    {
        var tablePrefix = NewPrefix();
        await RunScriptAsync(tablePrefix);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var logs = new CapturingLoggerProvider();

        var completed = await PostgreSqlSchemaVerifier.VerifyTracesAsync(
            connection, $"{tablePrefix}traces", $"{tablePrefix}spans", logs.CreateLogger("test"), new IntervalLogLimiter(TimeSpan.FromMinutes(1)), CancellationToken.None);

        completed.ShouldBeTrue();
        logs.Lines.ShouldBeEmpty();
    }

    private static string NewPrefix() => $"v{Guid.NewGuid():N}_";

    private static PostgreSqlStorageOptions OptionsWithoutAutoCreate(string tablePrefix) =>
        new() { TablePrefix = tablePrefix, AutoCreateSchema = false };

    private async Task RunScriptAsync(string tablePrefix)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(StorageSchemaScripts.PostgreSql(tablePrefix), connection);
        await command.ExecuteNonQueryAsync();
    }
}
